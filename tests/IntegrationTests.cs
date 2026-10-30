using System;
using TimeZoneSwitcher;

internal static class IntegrationTests
{
    private static int Main()
    {
        string original = TimeZoneService.ReadSystemId();
        bool originalDaylightDisabled = TimeZoneService.ReadDaylightDisabled();
        var service = new TimeZoneService(new ZoneCatalog(), new WindowsCommandRunner(), TimeZoneService.ReadSystemId);
        int status = 0;
        Console.WriteLine("ORIGINAL: " + original);
        try
        {
            foreach (string id in new[] { "Pacific Standard Time", "Eastern Standard Time", "China Standard Time" })
            {
                var result = service.Switch(id, false);
                Console.WriteLine(id + ": " + (result.Success ? "PASS" : "FAIL") + "; actual=" + result.ActualId + "; " + result.Message);
                if (!result.Success) { status = 1; break; }
            }
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); status = 1; }
        finally
        {
            var restored = service.Switch(original, false);
            if (originalDaylightDisabled)
            {
                var restoredFlag = new WindowsCommandRunner().Run(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tzutil.exe"), "/s \"" + original + "_dstoff\"", false);
                if (restoredFlag.ExitCode != 0 || !TimeZoneService.ReadDaylightDisabled()) status = 2;
            }
            Console.WriteLine("RESTORED: " + restored.ActualId + "; success=" + restored.Success);
            if (!restored.Success || TimeZoneService.ReadSystemId() != original) status = 2;
        }
        return status;
    }
}
