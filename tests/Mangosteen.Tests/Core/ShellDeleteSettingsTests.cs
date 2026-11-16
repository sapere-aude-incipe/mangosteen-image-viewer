namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class ShellDeleteSettingsTests
{
    [TestMethod]
    public void Windows_Confirms_Recycling_Unless_Its_No_Confirm_Flag_Is_Set()
    {
        // SHELLFLAGSTATE bit 2 is fNoConfirmRecycle; the other bits must not matter.
        Assert.IsTrue(ShellDeleteSettings.WindowsConfirmsRecycle(0));
        Assert.IsTrue(ShellDeleteSettings.WindowsConfirmsRecycle(0b11));
        Assert.IsFalse(ShellDeleteSettings.WindowsConfirmsRecycle(0b100));
        Assert.IsFalse(ShellDeleteSettings.WindowsConfirmsRecycle(0xFFFF_FFFF));
    }

    [TestMethod]
    public void Reading_The_Windows_Setting_Does_Not_Throw()
    {
        _ = ShellDeleteSettings.WindowsConfirmsRecycle();
    }
}
