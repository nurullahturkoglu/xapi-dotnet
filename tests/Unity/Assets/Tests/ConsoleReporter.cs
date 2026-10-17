using NUnit.Framework.Interfaces;
using UnityEngine;
using UnityEngine.TestRunner;

[assembly: TestRunCallback(typeof(Xapi.Unity.Tests.ConsoleReporter))]

namespace Xapi.Unity.Tests
{
    // Writes results to the player log, for players that run without an editor connection (a WebGL build in a
    // headless browser)
    public class ConsoleReporter : ITestRunCallback
    {
        public void RunStarted(ITest testsToRun)
        {
            Debug.Log($"XAPI-TEST run started: {testsToRun.TestCaseCount} tests");
        }

        public void RunFinished(ITestResult testResults)
        {
            Debug.Log($"XAPI-TEST run finished: {testResults.PassCount} passed, {testResults.FailCount} failed, {testResults.SkipCount} skipped");
        }

        public void TestStarted(ITest test)
        {
            if (!test.IsSuite)
            {
                Debug.Log($"XAPI-TEST started {test.FullName}");
            }
        }

        public void TestFinished(ITestResult result)
        {
            if (!result.Test.IsSuite)
            {
                Debug.Log($"XAPI-TEST {result.ResultState.Status} {result.Test.FullName} ({result.Duration:0.00}s) {result.Message} {result.StackTrace}");
            }
        }
    }
}
