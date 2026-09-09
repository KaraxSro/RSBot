using System;
using System.IO;
using RSBot.Core.Components;
using RSBot.Core.Event;

namespace RSBot.Core;

public class Log
{
    public static string SessionId => CoreLogFileSink.SessionId;
    public static string CurrentFilePath => CoreLogFileSink.CurrentFilePath;

    private static void Emit(LogLevel level, string message, string subsystem = "General", string operationId = null)
    {
        CoreLogFileSink.Enqueue(DateTime.Now, level, message, subsystem, operationId);
        EventManager.FireEvent("OnAddLog", message, level);
    }

    /// <summary>
    ///     Replaces the format item in a specified string with the string
    ///     representation of a corresponding object in a specified array
    /// </summary>
    /// <param name="logLevel">The message level</param>
    /// <param name="format">The format</param>
    /// <param name="args">The args</param>
    public static void AppendFormat(LogLevel logLevel, string format, params object[] args)
    {
        Emit(logLevel, string.Format(format, args));
    }

    /// <summary>
    ///     Appends the given message to the log using the provided log level.
    /// </summary>
    /// <param name="logLevel"></param>
    /// <param name="message"></param>
    public static void Append(LogLevel logLevel, string message)
    {
        Emit(logLevel, message);
    }

    public static void Append(LogLevel logLevel, string message, string subsystem, string operationId = null)
    {
        Emit(logLevel, message, subsystem, operationId);
    }

    /// <summary>
    ///     Appends the specified message.
    /// </summary>
    /// <param name="obj">The message.</param>
    /// <param name="level">The level.</param>
    public static void Notify(object obj)
    {
        Emit(LogLevel.Notify, obj.ToString());
    }

    /// <summary>
    ///     Appends the specified language key.
    /// </summary>
    /// <param name="obj">The message.</param>
    /// <param name="level">The level.</param>
    public static void NotifyLang(string key, params object[] args)
    {
        Emit(LogLevel.Notify, LanguageManager.GetLang(key, args));
    }

    /// <summary>
    ///     Append specified debug message
    /// </summary>
    /// <param name="obj">The message</param>
    public static void Debug(object obj)
    {
        Emit(LogLevel.Debug, obj.ToString());
    }

    /// <summary>
    ///     Append specified Warning message
    /// </summary>
    /// <param name="obj">The message</param>
    public static void Warn(object obj)
    {
        Emit(LogLevel.Warning, obj.ToString());
    }

    /// <summary>
    ///     Appends the specified language key.
    /// </summary>
    /// <param name="obj">The message.</param>
    /// <param name="level">The level.</param>
    public static void WarnLang(string key, params object[] args)
    {
        Emit(LogLevel.Warning, LanguageManager.GetLang(key, args));
    }

    /// <summary>
    ///     Append specified Error message
    /// </summary>
    /// <param name="obj">The message</param>
    public static void Error(object obj)
    {
        Emit(LogLevel.Error, obj.ToString());
    }

    /// <summary>
    ///     Change status message on ui
    /// </summary>
    /// <param name="obj">The message</param>
    public static void Status(object obj)
    {
        EventManager.FireEvent("OnChangeStatusText", obj.ToString());
    }

    /// <summary>
    ///     Change status message on ui by language key.
    /// </summary>
    /// <param name="obj">The message.</param>
    /// <param name="level">The level.</param>
    public static void StatusLang(string key, params object[] args)
    {
        EventManager.FireEvent("OnChangeStatusText", LanguageManager.GetLang(key, args));
    }

    /// <summary>
    ///     Append specified fatal message
    /// </summary>
    /// <param name="obj">The message</param>
    public static void Fatal(Exception obj)
    {
        Emit(LogLevel.Error, obj.ToString(), "UnhandledException");

        var filePath = Path.Combine(Kernel.BasePath, "Data", "Logs", "Exceptions", $"{DateTime.Now:dd-MM-yyyy}.txt");
        if (!Directory.Exists(Path.GetDirectoryName(filePath)))
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));

        using (var stream = File.AppendText(filePath))
        {
            stream.WriteLine(obj.ToString());
        }
    }
}
