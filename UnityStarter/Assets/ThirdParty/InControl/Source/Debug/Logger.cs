namespace InControl
{
	using System;


	public enum LogMessageType
	{
		Info,
		Warning,
		Error
	}


	public struct LogMessage
	{
		public string Text;
		public LogMessageType Type;
	}


	public static class Logger
	{
		public static event Action<LogMessage> OnLogMessage;


		public static void LogInfo( string text )
		{
			if (OnLogMessage != null)
			{
				var logMessage = new LogMessage { Text = text, Type = LogMessageType.Info };
				OnLogMessage( logMessage );
			}
		}


		public static void LogWarning( string text )
		{
			if (OnLogMessage != null)
			{
				var logMessage = new LogMessage { Text = text, Type = LogMessageType.Warning };
				OnLogMessage( logMessage );
			}
		}


		public static void LogError( string text )
		{
			if (OnLogMessage != null)
			{
				var logMessage = new LogMessage { Text = text, Type = LogMessageType.Error };
				OnLogMessage( logMessage );
			}
		}
	}
}
