using System.Text;
using SaveHealth;
using Xunit;

namespace SfSaveHealth.Tests;

/// <summary>0.7.2: what happens when the tool can't write: the report on stdout, solo's saved copy, and solo's
/// temporary folder.</summary>
public class WriteFailureTests
{
    static readonly Solo.Outcome Sample = new("short report\n", "full report with every quest change\n", 1, "1 new warning(s): worth a look.");

    [Fact]
    public void A_stdout_that_fails_still_leaves_the_saved_solo_report()
    {
        using var d = new TempDir();
        string dir = Path.Combine(d.Path, "reports"), report = Path.Combine(dir, "x.txt");
        var old = Console.Out;
        Console.SetOut(new ClosedWriter());
        try
        {
            var e = Assert.Throws<UserError>(() => Output.FinishSolo(Sample, dir, report));
            Assert.StartsWith("couldn't write the report to the output", e.Message);
            Assert.EndsWith($"; the full report was saved to {report}", e.Fix);   // the user learns where the copy is
        }
        finally { Console.SetOut(old); }
        // The saved copy was written before the failed print, and holds the full report.
        Assert.Equal(Sample.FullText, File.ReadAllText(report));
    }

    [Fact]
    public void A_stdout_that_fails_exits_3_and_logs_the_saved_copy()
    {
        using var d = new TempDir();
        string dir = Path.Combine(d.Path, "reports"), report = Path.Combine(dir, "x.txt"), log = Path.Combine(d.Path, "run.log");
        var old = Console.Out;
        Log.Open(log, false);
        Console.SetOut(new ClosedWriter());
        try { Assert.Throws<UserError>(() => Output.FinishSolo(Sample, dir, report)); }
        finally { Console.SetOut(old); Log.Close(); }
        Assert.Contains($"solo: report saved to {report}", File.ReadAllText(log));
    }

    [Fact]
    public void A_working_stdout_prints_the_short_report_then_the_saved_line()
    {
        using var d = new TempDir();
        string dir = Path.Combine(d.Path, "reports"), report = Path.Combine(dir, "x.txt");
        var output = new StringWriter();
        var old = Console.Out;
        Console.SetOut(output);
        int code;
        try { code = Output.FinishSolo(Sample, dir, report); }
        finally { Console.SetOut(old); }
        Assert.Equal(1, code);
        Assert.Equal("short report\n" + $"Report saved to {report}" + Environment.NewLine, output.ToString());
        Assert.Equal(Sample.FullText, File.ReadAllText(report));
    }

    [Fact]
    public void A_report_folder_that_cant_be_made_keeps_the_exit_code_and_names_the_fix()
    {
        using var d = new TempDir();
        // A file where the folder should be: the folder can't be made (IOException).
        string dir = d.File("reports"), report = Path.Combine(dir, "x.txt"), log = Path.Combine(d.Path, "run.log");
        var (output, error) = (new StringWriter(), new StringWriter());
        var (oldOut, oldErr) = (Console.Out, Console.Error);
        Log.Open(log, false);
        Console.SetOut(output);
        Console.SetError(error);
        int code;
        try { code = Output.FinishSolo(Sample, dir, report); }
        finally { Console.SetOut(oldOut); Console.SetError(oldErr); Log.Close(); }
        Assert.Equal(1, code);
        Assert.Equal("short report\n", output.ToString());
        Assert.StartsWith($"error: couldn't save the report to {report}: ", error.ToString());
        Assert.Contains("  fix: " + Output.SaveFix, error.ToString());
        Assert.Contains($"solo: report not saved to {report}: IOException", File.ReadAllText(log));
    }

    [Fact]
    public void A_failed_save_and_a_failed_stdout_both_reach_the_error()
    {
        using var d = new TempDir();
        string dir = d.File("reports"), report = Path.Combine(dir, "x.txt");
        var old = Console.Out;
        Console.SetOut(new ClosedWriter());
        UserError e;
        try { e = Assert.Throws<UserError>(() => Output.FinishSolo(Sample, dir, report)); }
        finally { Console.SetOut(old); }
        Assert.StartsWith("couldn't write the report to the output", e.Message);
        Assert.Contains($"; the full report couldn't be saved to {report} either: ", e.Fix);
    }

    [Fact]
    public void The_write_fix_lines_name_room_and_access_rights()
    {
        Assert.Contains("drive has room", Output.SaveFix);
        Assert.Contains("you may write there", Output.SaveFix);
        Assert.Contains("--reports", Output.SaveFix);
        var old = Console.Out;
        Console.SetOut(new ClosedWriter());
        try
        {
            var e = Assert.Throws<UserError>(() => Output.WriteReport("a report"));
            Assert.Contains("drive has room", e.Fix);
            Assert.Contains("you may write to it", e.Fix);
        }
        finally { Console.SetOut(old); }
    }

    [Fact]
    public void A_report_folder_without_access_rights_gets_the_same_fix()
    {
        // UnauthorizedAccessException takes the same path as IOException: the fix names access rights.
        using var d = new TempDir();
        var error = new StringWriter();
        var (oldOut, oldErr) = (Console.Out, Console.Error);
        Console.SetOut(new StringWriter());
        Console.SetError(error);
        string dir = Path.Combine(d.Path, "reports"), report = Path.Combine(dir, "x.txt");
        Directory.CreateDirectory(report);   // a folder where the report file should be: Windows refuses with access denied
        try { Assert.Equal(1, Output.FinishSolo(Sample, dir, report)); }
        finally { Console.SetOut(oldOut); Console.SetError(oldErr); }
        Assert.Contains("  fix: " + Output.SaveFix, error.ToString());
    }

    [Fact]
    public void Removing_a_temp_folder_with_a_read_only_file_never_throws()
    {
        using var d = new TempDir();
        string tmp = Path.Combine(d.Path, "solo-tmp"), log = Path.Combine(d.Path, "run.log");
        Directory.CreateDirectory(tmp);
        string file = Path.Combine(tmp, "before.sfs");
        File.WriteAllBytes(file, [1, 2, 3]);
        File.SetAttributes(file, FileAttributes.ReadOnly);
        try
        {
            // On Windows a read-only file stops Directory.Delete with UnauthorizedAccessException, which 0.7.1's
            // cleanup (IOException only) let through. Linux deletes it (the folder is writable).
            if (OperatingSystem.IsWindows())
                Assert.Throws<UnauthorizedAccessException>(() => Directory.Delete(tmp, true));
            Log.Open(log, false);
            try { Solo.RemoveTemp(tmp); }
            finally { Log.Close(); }
            if (OperatingSystem.IsWindows())
                Assert.Contains($"could not remove {tmp}: UnauthorizedAccessException", File.ReadAllText(log));
        }
        finally { if (File.Exists(file)) File.SetAttributes(file, FileAttributes.Normal); }
    }

    [Fact]
    public void Removing_a_temp_folder_with_a_file_held_open_never_throws()
    {
        using var d = new TempDir();
        string tmp = Path.Combine(d.Path, "solo-tmp"), log = Path.Combine(d.Path, "run.log");
        Directory.CreateDirectory(tmp);
        using (var held = new FileStream(Path.Combine(tmp, "after.sfs"), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            Log.Open(log, false);
            try { Solo.RemoveTemp(tmp); }
            finally { Log.Close(); }
        }
        if (OperatingSystem.IsWindows())
            Assert.Contains($"could not remove {tmp}: IOException", File.ReadAllText(log));
        Solo.RemoveTemp(tmp);   // once nothing holds the file, the folder goes
        Assert.False(Directory.Exists(tmp));
    }

    [Fact]
    public void Removing_a_temp_folder_that_is_gone_never_throws()
    {
        using var d = new TempDir();
        Solo.RemoveTemp(Path.Combine(d.Path, "never-made"));
    }

    /// <summary>A console stream whose reader went away (a closed pipe): every write fails.</summary>
    sealed class ClosedWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char value) => throw new IOException("The pipe has been ended.");
        public override void Write(string? value) => throw new IOException("The pipe has been ended.");
        public override void Write(char[] buffer, int index, int count) => throw new IOException("The pipe has been ended.");
        public override void WriteLine(string? value) => throw new IOException("The pipe has been ended.");
    }
}
