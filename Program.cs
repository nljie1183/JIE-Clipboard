using System.Runtime.InteropServices;
using JIE剪切板.Native;
using JIE剪切板.Services;

namespace JIE剪切板;

internal static class Program
{
    private static IntPtr _mutexHandle;

    [STAThread]
    static void Main()
    {
        // Single instance check
        _mutexHandle = Win32Api.CreateMutex(IntPtr.Zero, true, "JIE剪切板_SingleInstance_Mutex");
        if (Marshal.GetLastWin32Error() == Win32Api.ERROR_ALREADY_EXISTS)
        {
            MessageBox.Show("JIE剪切板 已在运行中。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Global exception handlers
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += Application_ThreadException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());

        // Cleanup mutex
        if (_mutexHandle != IntPtr.Zero)
        {
            Win32Api.ReleaseMutex(_mutexHandle);
            Win32Api.CloseHandle(_mutexHandle);
        }
    }

    private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
    {
        try
        {
            LogService.Log("UI thread unhandled exception", e.Exception);
            MessageBox.Show(
                $"应用程序发生未处理的异常：\n{e.Exception.Message}\n\n详细信息已记录到日志。",
                "错误",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch { }
    }

    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            var ex = e.ExceptionObject as Exception;
            LogService.Log("AppDomain unhandled exception", ex);
            MessageBox.Show(
                $"应用程序发生严重错误：\n{ex?.Message}\n\n详细信息已记录到日志。",
                "严重错误",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch { }
    }
}
