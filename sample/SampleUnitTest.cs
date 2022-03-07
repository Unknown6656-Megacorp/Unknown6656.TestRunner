using Microsoft.VisualStudio.TestTools.UnitTesting;

using Unknown6656.Testing;


return UnitTestRunner.RunTests();




[TestClass]
public class SampleUnitTest
    : UnitTestRunner
{
    public override void Test_StaticInit()
    {
        // this will be executed once before all tests
    }

    public override void Test_Init()
    {
        // this will be executed before each test
    }

    public override void Test_Cleanup()
    {
        // this will be executed after each test
    }

    public override void Test_StaticCleanup()
    {
        // this will be executed once after all tests
    }


    [TestMethod]
    [TestWith(1, 2, 3)]
    [TestWith(10, -10, 0)]
    [TestWith(-1, 2, 1)]
    public void Test_Addition(int a, int b, int result) => Assert.AreEqual(a + b, result);

    [TestMethod]
    public void This_Method_will_always_succeed() => Assert.IsTrue(true);

    [TestMethod]
    public void This_Method_will_always_fail() => Assert.Fail();

    public void This_Method_will_not_be_tested()
    {
    }

    [TestMethod]
    public void This_Method_will_be_skipped() => Skip();

    [TestMethod, Skip]
    public void This_Method_will_also_be_skipped()
    {
    }
}
