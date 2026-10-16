#!/usr/bin/env node
// Exports the template-generated statement cases of adlnet/lrs-conformance-test-suite
// as JSON test data. See README.md in this folder.
//
// usage: node eng/cts-port/export-templates.js <suite-dir> [--out <dir>] [--commit <sha>]
'use strict';

// Must be set before any Date is created: formatting.js builds Date objects from local-time strings.
process.env.TZ = 'UTC';

const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const FIXED_NOW = Date.parse('2026-01-01T00:00:00.000Z');
const FIXED_UUID = '00000000-0000-4000-8000-000000000000';

const BATTERIES = [
  { version: '1.0.3', folder: 'v1_0_3', expectedCount: 950 },
  { version: '2.0.0', folder: 'v2_0', expectedCount: 972 },
];

// Cases whose suite outcome is not decided by the statement JSON alone (see README.md).
const KIND_RULES = [
  { config: 'timestamp_property.js', test: /future date/, kind: 'time-dependent' },
];

function main() {
  const args = parseArgs(process.argv.slice(2));
  const suiteDir = path.resolve(args.suite);
  const outDir = path.resolve(args.out ?? path.join(__dirname, '..', '..', 'tests', 'Xapi.Core.Tests', 'Conformance'));
  const commit = args.commit ?? readCommit(suiteDir);
  const license = readLicense(suiteDir);

  fs.mkdirSync(outDir, { recursive: true });
  for (const battery of BATTERIES) {
    const cases = exportBattery(suiteDir, battery);
    if (cases.length !== battery.expectedCount) {
      throw new Error(`${battery.version}: exported ${cases.length} cases, expected ${battery.expectedCount}`);
    }
    const doc = {
      source: 'adlnet/lrs-conformance-test-suite',
      commit,
      license,
      version: battery.version,
      cases,
    };
    const file = path.join(outDir, `cts-templates-${battery.version}.json`);
    fs.writeFileSync(file, JSON.stringify(doc, null, 2) + '\n');
    const kinds = countBy(cases, c => `${c.kind}/${c.expect}`);
    console.log(`${path.relative(process.cwd(), file)}: ${cases.length} cases ${JSON.stringify(kinds)}`);
  }
}

function parseArgs(argv) {
  const result = {};
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === '--out' || arg === '--commit') {
      if (i + 1 >= argv.length) usage(`${arg} needs a value`);
      result[arg.slice(2)] = argv[++i];
    } else if (arg.startsWith('--')) {
      usage(`unknown option ${arg}`);
    } else if (result.suite === undefined) {
      result.suite = arg;
    } else {
      usage(`unexpected argument ${arg}`);
    }
  }
  if (result.suite === undefined) usage('missing <suite-dir>');
  return result;
}

function usage(message) {
  console.error(`${message}\nusage: node export-templates.js <suite-dir> [--out <dir>] [--commit <sha>]`);
  process.exit(2);
}

function readCommit(suiteDir) {
  const gitDir = path.join(suiteDir, '.git');
  let ref = fs.readFileSync(path.join(gitDir, 'HEAD'), 'utf8').trim();
  if (ref.startsWith('ref: ')) {
    const name = ref.slice(5);
    const loose = path.join(gitDir, name);
    if (fs.existsSync(loose)) {
      ref = fs.readFileSync(loose, 'utf8').trim();
    } else {
      const packed = fs.readFileSync(path.join(gitDir, 'packed-refs'), 'utf8')
        .split('\n')
        .map(line => line.split(' '))
        .find(parts => parts[1] === name);
      if (!packed) throw new Error(`cannot resolve ${name}; pass --commit`);
      ref = packed[0];
    }
  }
  if (!/^[0-9a-f]{40}$/.test(ref)) throw new Error(`unexpected HEAD "${ref}"; pass --commit`);
  return ref;
}

function readLicense(suiteDir) {
  const text = fs.readFileSync(path.join(suiteDir, 'LICENSE'), 'utf8');
  if (!/^The MIT License/.test(text)) throw new Error('suite LICENSE is no longer MIT; review before exporting');
  return 'MIT';
}

function exportBattery(suiteDir, battery) {
  const testDir = path.join(suiteDir, 'test', battery.folder);
  const templates = loadTemplates(path.join(testDir, 'templates'));
  const cases = [];

  // The runner adds test files in readdir order and Mocha runs them in that order.
  const testFiles = fs.readdirSync(testDir).filter(f => f.endsWith('.js')).sort(compareOrdinal);
  for (const testFile of testFiles) {
    const source = fs.readFileSync(path.join(testDir, testFile), 'utf8');
    for (const call of findTemplateCalls(source, testFile)) {
      const configs = loadConfig(path.join(testDir, 'configs', call.config));
      configs.forEach((requirement, r) => {
        requirement.config.forEach((test, t) => {
          const statement = buildStatement(test, templates, `${call.config} ${requirement.name} / ${test.name}`);
          const expect = toExpect(test.expect, test.name);
          cases.push({
            id: `${call.config.replace(/\.js$/, '')}-${pad(r + 1)}-${pad(t + 1)}`,
            path: [...call.describes, requirement.name, test.name],
            requirement: requirementId([requirement.name, ...call.describes.toReversed()]),
            kind: classify(call.config, test.name),
            expect,
            statement,
          });
        });
      });
    }
  }

  const ids = new Set();
  for (const c of cases) {
    if (ids.has(c.id)) throw new Error(`duplicate id ${c.id}`);
    ids.add(c.id);
  }
  return cases;
}

function compareOrdinal(a, b) {
  return a < b ? -1 : a > b ? 1 : 0;
}

function pad(n) {
  return String(n).padStart(3, '0');
}

function countBy(items, key) {
  const result = {};
  for (const item of items) result[key(item)] = (result[key(item)] ?? 0) + 1;
  return result;
}

function toExpect(expect, name) {
  if (!Array.isArray(expect) || expect.length !== 1) throw new Error(`unsupported expect in "${name}"`);
  if (expect[0] === 200 || expect[0] === 204) return 'valid';
  if (expect[0] === 400) return 'invalid';
  throw new Error(`unsupported status ${expect[0]} in "${name}"`);
}

// The innermost title that names a requirement wins (verify.js inherits XAPI-00014 from its
// describe). A few titles name two requirements (e.g. "XAPI-00008, XAPI-00010"); the first is kept.
function requirementId(titles) {
  for (const title of titles) {
    const ids = title.match(/XAPI-\d{5}/g);
    if (ids) return ids[0];
  }
  return null;
}

function classify(config, testName) {
  const rule = KIND_RULES.find(r => r.config === config && r.test.test(testName));
  return rule ? rule.kind : 'statement-validity';
}

// helper.getJsonMapping: templates/<folder>/<name>.json, addressed as {{folder.name}}.
function loadTemplates(templateDir) {
  const mapping = {};
  for (const folder of fs.readdirSync(templateDir).sort(compareOrdinal)) {
    const folderPath = path.join(templateDir, folder);
    if (!fs.statSync(folderPath).isDirectory()) continue;
    const files = {};
    for (const file of fs.readdirSync(folderPath).sort(compareOrdinal)) {
      if (file.indexOf('.json') <= 0) continue;
      files[file.substring(0, file.indexOf('.json'))] = JSON.parse(fs.readFileSync(path.join(folderPath, file), 'utf8'));
    }
    mapping[folder] = files;
  }
  return mapping;
}

// Evaluates a configs/*.js file in an isolated context. The only thing configs take from
// helper.js is generateUUID, and the only clock reads are new Date(); both are pinned.
function loadConfig(configPath) {
  const sandboxModule = { exports: {} };
  const helperStub = Object.freeze({ generateUUID: () => FIXED_UUID });
  const context = vm.createContext({
    module: sandboxModule,
    require: name => {
      if (name === './../../helper.js') return helperStub;
      throw new Error(`${path.basename(configPath)} requires ${name}`);
    },
  });
  vm.runInContext(
    `Date = class extends Date {
       constructor(...args) { if (args.length === 0) super(${FIXED_NOW}); else super(...args); }
       static now() { return ${FIXED_NOW}; }
     };`,
    context);
  vm.runInContext(fs.readFileSync(configPath, 'utf8'), context, { filename: configPath });
  return sandboxModule.exports.config();
}

// templatingSelection.createTemplate: helper.convertTemplate + helper.createTestObject,
// then the value under the first template's key is POSTed with JSON.stringify.
function buildStatement(test, templates, label) {
  let data;
  if (test.templates) {
    // getJsonMapping runs once per test, so two references to the same template share one object.
    const mapping = new Map();
    const converted = test.templates.map(item => {
      const key = Object.keys(item)[0];
      const value = item[key];
      if (typeof value === 'string' && value.indexOf('{{') === 0 && value.indexOf('}}') === value.length - 2) {
        if (!mapping.has(value)) mapping.set(value, resolveTemplate(templates, value));
        return { [key]: mapping.get(value) };
      }
      return item;
    });
    const mock = createTestObject(converted);
    data = mock[String(Object.keys(mock))];
  } else if (test.json) {
    data = test.json;
  } else {
    throw new Error(`invalid test ${label}`);
  }
  const json = JSON.stringify(data);
  if (json === undefined) throw new Error(`no body for ${label}`);
  return JSON.parse(json);
}

function resolveTemplate(templates, reference) {
  let node = templates;
  for (const part of reference.slice(2, -2).split('.')) {
    node = node[part];
    if (!node) throw new Error(`Not mapped: ${reference}`);
  }
  return extend(true, {}, node);
}

function createTestObject(array) {
  let from = {};
  array.reverse().forEach((to, index) => {
    if (index === 0) {
      from = to;
      return;
    }
    extend(true, to[Object.keys(to)[0]], from);
    from = to;
  });
  return from;
}

// Same semantics as the `extend` npm package (2.x) the suite uses: deep merge in place,
// arrays merged index by index, undefined skipped, non-plain objects (Date) copied by reference.
function extend(deep, target, ...sources) {
  if (target == null || (typeof target !== 'object' && typeof target !== 'function')) target = {};
  for (const options of sources) {
    if (options == null) continue;
    for (const name in options) {
      const src = target[name];
      const copy = options[name];
      if (target === copy) continue;
      const copyIsArray = Array.isArray(copy);
      if (deep && copy && (isPlainObject(copy) || copyIsArray)) {
        const clone = copyIsArray
          ? (src && Array.isArray(src) ? src : [])
          : (src && isPlainObject(src) ? src : {});
        setProperty(target, name, extend(deep, clone, copy));
      } else if (copy !== undefined) {
        setProperty(target, name, copy);
      }
    }
  }
  return target;
}

function isPlainObject(obj) {
  if (!obj || Object.prototype.toString.call(obj) !== '[object Object]') return false;
  const hasOwnConstructor = Object.prototype.hasOwnProperty.call(obj, 'constructor');
  const hasIsPrototypeOf = obj.constructor && obj.constructor.prototype
    && Object.prototype.hasOwnProperty.call(obj.constructor.prototype, 'isPrototypeOf');
  if (obj.constructor && !hasOwnConstructor && !hasIsPrototypeOf) return false;
  let key;
  for (key in obj) { /* find the last key */ }
  return key === undefined || Object.prototype.hasOwnProperty.call(obj, key);
}

function setProperty(target, name, value) {
  if (name === '__proto__') {
    Object.defineProperty(target, name, { enumerable: true, configurable: true, writable: true, value });
  } else {
    target[name] = value;
  }
}

// Finds templatingSelection.createTemplate('<config>') calls outside comments and the titles of
// the describe() blocks that enclose each one, without executing the test file.
function findTemplateCalls(source, fileName) {
  const calls = [];
  const stack = [];
  let pendingDescribe = null;
  let lastSignificant = '';
  let i = 0;

  const readString = quote => {
    let value = '';
    i++;
    while (i < source.length && source[i] !== quote) {
      if (source[i] === '\\') {
        const next = source[i + 1];
        const escapes = { n: '\n', t: '\t', r: '\r', b: '\b', f: '\f', v: '\v', 0: '\0' };
        if (next === 'u') {
          value += String.fromCharCode(parseInt(source.substr(i + 2, 4), 16));
          i += 6;
          continue;
        }
        value += escapes[next] ?? next;
        i += 2;
        continue;
      }
      value += source[i++];
    }
    i++;
    return value;
  };

  const skipSpace = () => {
    while (i < source.length && /\s/.test(source[i])) i++;
  };

  while (i < source.length) {
    const ch = source[i];
    if (ch === '/' && source[i + 1] === '/') {
      i = source.indexOf('\n', i);
      if (i < 0) break;
      continue;
    }
    if (ch === '/' && source[i + 1] === '*') {
      i = source.indexOf('*/', i + 2) + 2;
      continue;
    }
    if (ch === '\'' || ch === '"' || ch === '`') {
      readString(ch);
      lastSignificant = 'string';
      continue;
    }
    if (ch === '/' && (lastSignificant === '' || /^[(,=:[!&|?{};]$/.test(lastSignificant))) {
      i++;
      let inClass = false;
      while (i < source.length && (source[i] !== '/' || inClass)) {
        if (source[i] === '\\') i++;
        else if (source[i] === '[') inClass = true;
        else if (source[i] === ']') inClass = false;
        i++;
      }
      i++;
      lastSignificant = 'regex';
      continue;
    }
    if (/[A-Za-z_$]/.test(ch)) {
      const start = i;
      while (i < source.length && /[\w$.]/.test(source[i])) i++;
      const word = source.slice(start, i);
      skipSpace();
      if (source[i] === '(' && (word === 'describe' || /(^|\.)createTemplate$/.test(word))) {
        i++;
        skipSpace();
        const quote = source[i];
        if (quote !== '\'' && quote !== '"' && quote !== '`') {
          throw new Error(`${fileName}: ${word}() without a literal first argument at offset ${start}`);
        }
        const value = readString(quote);
        if (word === 'describe') {
          stack.push({ kind: '(', describe: value });
          pendingDescribe = stack[stack.length - 1];
        } else {
          stack.push({ kind: '(' });
          calls.push({ config: value, describes: stack.filter(s => s.body).map(s => s.body) });
        }
        lastSignificant = 'string';
        continue;
      }
      lastSignificant = 'word';
      continue;
    }
    if (ch === '(' || ch === '[') {
      stack.push({ kind: ch });
    } else if (ch === '{') {
      const entry = { kind: '{' };
      const nearestParen = [...stack].reverse().find(s => s.kind === '(');
      if (pendingDescribe && nearestParen === pendingDescribe) {
        entry.body = pendingDescribe.describe;
        pendingDescribe = null;
      }
      stack.push(entry);
    } else if (ch === ')' || ch === ']' || ch === '}') {
      const top = stack.pop();
      const expected = { ')': '(', ']': '[', '}': '{' }[ch];
      if (!top || top.kind !== expected) throw new Error(`${fileName}: unbalanced "${ch}" at offset ${i}`);
    }
    if (!/\s/.test(ch)) lastSignificant = ch;
    i++;
  }
  if (stack.length !== 0) throw new Error(`${fileName}: ${stack.length} unclosed brackets`);
  return calls;
}

main();
