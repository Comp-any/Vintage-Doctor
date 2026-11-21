#define TRACE
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Vintagestory;

public abstract class Logger : LoggerBase, IDisposable
{
	private string program = "";

	protected Dictionary<string, DisposableWriter> fileWriters = new Dictionary<string, DisposableWriter>();

	protected Dictionary<string, uint> LinesWritten = new Dictionary<string, uint>();

	protected Dictionary<string, int> LogfileSplitNumbers = new Dictionary<string, int>();

	protected bool exceptionPrinted;

	protected bool disposed;

	protected bool canUseColor = true;

	private static string[] logTypeToExcludeFromArchive = new string[2] { "crash", "worldgen" };

	public const string ArchiveFolderDateFormat = "yyyy-MM-dd_HH_mm_ss";

	public const string LogDateFormat = "d.M.yyyy HH:mm:ss";

	public const string LogDateFormatVerbose = "d.M.yyyy HH:mm:ss.fff";

	private static bool _logsRotated;

	public static uint LogFileSplitAfterLine = 500000u;

	public Logger(string program, bool clearOldFiles, int archiveLogFileCount, int archiveLogFileMaxSize)
	{
		this.program = program;
		if (clearOldFiles && !_logsRotated)
		{
			ArchiveLogFiles(archiveLogFileCount, archiveLogFileMaxSize);
		}
		foreach (EnumLogType value in Enum.GetValues(typeof(EnumLogType)))
		{
			string logFile = getLogFile(value);
			if (logFile != null && !fileWriters.ContainsKey(logFile))
			{
				try
				{
					fileWriters.Add(logFile, new DisposableWriter(logFile, value != EnumLogType.Worldgen && clearOldFiles));
					LinesWritten.Add(logFile, 0u);
					LogfileSplitNumbers.Add(logFile, 2);
				}
				catch (Exception e)
				{
					Error("Cannot open logfile {0} for writing ", logFile);
					Error(e);
				}
			}
		}
		Notification("{0} logger started.", program);
		Notification("Game Version: {0}", GameVersion.LongGameVersion);
	}

	public static string GetTypeName(Type type)
	{
		if (type.IsGenericType)
		{
			string obj = type.Name.Split(new char[1] { '`' }, StringSplitOptions.RemoveEmptyEntries)[0];
			string text = type.GetGenericArguments().Select(GetTypeName).Aggregate((string first, string second) => first + "," + second);
			return obj + "<" + text + ">";
		}
		return type.Name;
	}

	private static void ArchiveLogFiles(int archiveLogFileCount, int archiveLogFileMaxSize)
	{
		_logsRotated = true;
		if (!Directory.Exists(GamePaths.Logs))
		{
			Directory.CreateDirectory(GamePaths.Logs);
			return;
		}
		List<FileInfo> list = (from folder in Directory.GetFiles(GamePaths.Logs, "*.log")
			where !logTypeToExcludeFromArchive.Any(folder.Contains)
			select new FileInfo(folder) into file
			orderby file.LastWriteTime descending
			select file).ToList();
		if (list.Count <= 0)
		{
			return;
		}
		string sessionTimeStamp = GetSessionTimeStamp(list.First().FullName);
		string path = Path.Combine(GamePaths.Logs, "Archive");
		Directory.CreateDirectory(path);
		string text = Path.Combine(GamePaths.Logs, "Archive", sessionTimeStamp);
		Directory.CreateDirectory(text);
		bool flag = list.Sum((FileInfo file) => file.Length / 1024 / 1024) > archiveLogFileMaxSize;
		foreach (FileInfo item in list)
		{
			try
			{
				if (flag)
				{
					File.Delete(item.FullName);
				}
				else
				{
					File.Move(item.FullName, Path.Combine(text, item.Name));
				}
			}
			catch (Exception)
			{
			}
		}
		List<DirectoryInfo> list2 = (from folder in Directory.GetDirectories(path)
			select new DirectoryInfo(folder)).ToList();
		if (list2.Count < archiveLogFileCount)
		{
			return;
		}
		foreach (DirectoryInfo item2 in list2.OrderBy((DirectoryInfo folder) => folder.CreationTime).Take(list2.Count - archiveLogFileCount))
		{
			string[] files = Directory.GetFiles(item2.FullName);
			for (int num = 0; num < files.Length; num++)
			{
				File.Delete(files[num]);
			}
			Directory.Delete(item2.FullName);
		}
	}

	private static string GetSessionTimeStamp(string logFile)
	{
		string text = null;
		IEnumerator<string> enumerator = ReadLines(logFile).GetEnumerator();
		enumerator.MoveNext();
		string text2 = string.Join(' ', enumerator.Current?.Split(' ').Take(2) ?? Array.Empty<string>());
		enumerator.Dispose();
		if (string.IsNullOrWhiteSpace(text2))
		{
			return DateTime.Now.ToString("yyyy-MM-dd_HH_mm_ss");
		}
		if (DateTime.TryParseExact(text2, "d.M.yyyy HH:mm:ss", null, DateTimeStyles.None, out var result))
		{
			return result.ToString("yyyy-MM-dd_HH_mm_ss", CultureInfo.InvariantCulture);
		}
		return DateTime.UtcNow.ToString("yyyy-MM-dd_HH_mm_ss");
	}

	public static IEnumerable<string> ReadLines(string path)
	{
		using FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.SequentialScan);
		using StreamReader sr = new StreamReader(fs, Encoding.UTF8);
		string text;
		while ((text = sr.ReadLine()) != null)
		{
			yield return text;
		}
	}

	public void Dispose()
	{
		foreach (DisposableWriter value in fileWriters.Values)
		{
			value.Dispose();
		}
		fileWriters.Clear();
		disposed = true;
	}

	public abstract string getLogFile(EnumLogType logType);

	public abstract bool printToConsole(EnumLogType logType);

	public abstract bool printToDebugWindow(EnumLogType logType);

	protected override void LogImpl(EnumLogType logType, string message, params object[] args)
	{
		if (disposed)
		{
			return;
		}
		try
		{
			string logFile = getLogFile(logType);
			if (logFile != null)
			{
				try
				{
					LogToFile(logFile, logType, message, args);
				}
				catch (NotSupportedException)
				{
					Console.WriteLine("Unable to write to log file " + logFile);
				}
				catch (ObjectDisposedException)
				{
					Console.WriteLine("Unable to write to log file " + logFile);
				}
			}
			if (TraceLog && printToDebugWindow(logType))
			{
				Trace.WriteLine(FormatLogEntry(logType, message, args));
			}
			if (printToConsole(logType))
			{
				SetColorForLogType(logType);
				Console.WriteLine(FormatLogEntry(logType, message, args));
				if (canUseColor)
				{
					Console.ResetColor();
				}
			}
		}
		catch (Exception)
		{
		}
	}

	public string FormatLogEntry(EnumLogType logType, string message, params object[] args)
	{
		return string.Format(DateTime.Now.ToString((logType == EnumLogType.VerboseDebug) ? "d.M.yyyy HH:mm:ss.fff" : "d.M.yyyy HH:mm:ss") + " [" + program + " " + logType.ToString() + "] " + message, args);
	}

	public virtual void LogToFile(string logFileName, EnumLogType logType, string message, params object[] args)
	{
		if (!fileWriters.ContainsKey(logFileName) || disposed)
		{
			return;
		}
		try
		{
			LinesWritten[logFileName]++;
			if (LinesWritten[logFileName] > LogFileSplitAfterLine)
			{
				LinesWritten[logFileName] = 0u;
				string filename = $"{logFileName.Replace(".log", "")}-{LogfileSplitNumbers[logFileName]}.log";
				fileWriters[logFileName].Dispose();
				fileWriters[logFileName] = new DisposableWriter(filename, clearOldFiles: true);
				LogfileSplitNumbers[logFileName]++;
			}
			string text = logType.ToString() ?? "";
			if (logType == EnumLogType.StoryEvent)
			{
				text = "Event";
			}
			fileWriters[logFileName].WriteLine(DateTime.Now.ToString((logType == EnumLogType.VerboseDebug) ? "d.M.yyyy HH:mm:ss.fff" : "d.M.yyyy HH:mm:ss") + " [" + text + "] " + message, args);
		}
		catch (FormatException)
		{
			if (!exceptionPrinted)
			{
				exceptionPrinted = true;
				Error("Couldn't write to log file, failed formatting {0} (FormatException)", message);
			}
		}
		catch (Exception e)
		{
			if (!exceptionPrinted)
			{
				exceptionPrinted = true;
				Error("Couldn't write to log file {0}!", logFileName);
				Error(e);
			}
		}
	}

	private void SetColorForLogType(EnumLogType logType)
	{
		if (!canUseColor)
		{
			return;
		}
		try
		{
			switch (logType)
			{
			case EnumLogType.Error:
			case EnumLogType.Fatal:
				Console.ForegroundColor = ConsoleColor.Red;
				break;
			case EnumLogType.Warning:
				Console.ForegroundColor = ConsoleColor.DarkYellow;
				break;
			}
		}
		catch (Exception)
		{
			canUseColor = false;
		}
	}
}
