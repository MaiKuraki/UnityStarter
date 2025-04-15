namespace InControl.Internal
{
	using System.Linq;
	using System.Text;
	using System.Text.RegularExpressions;


	public class CodeWriter
	{
		const char newLine = '\n';
		int indent;
		readonly StringBuilder stringBuilder;


		public CodeWriter()
		{
			indent = 0;
			stringBuilder = new StringBuilder( 4096 );
		}


		public void IncreaseIndent()
		{
			indent += 1;
		}


		public void DecreaseIndent()
		{
			indent -= 1;
		}


		public void Append( string code )
		{
			Append( false, code );
		}


		public void Append( bool trim, string code )
		{
			if (trim)
			{
				code = code.Trim();
			}

			var lines = Regex.Split( code, @"\r?\n|\n" );
			var linesCount = lines.Length;
			for (var i = 0; i < linesCount; i++)
			{
				var line = lines[i];

				if (!line.All( char.IsWhiteSpace ))
				{
					stringBuilder.Append( '\t', indent );
					stringBuilder.Append( line );
				}

				if (i < linesCount - 1)
				{
					stringBuilder.Append( newLine );
				}
			}
		}


		public void AppendLine( string code )
		{
			Append( code );
			stringBuilder.Append( newLine );
		}


		public void AppendLine( int count )
		{
			stringBuilder.Append( newLine, count );
		}


		public void AppendFormat( string format, params object[] args )
		{
			Append( string.Format( format, args ) );
		}


		public void AppendLineFormat( string format, params object[] args )
		{
			AppendLine( string.Format( format, args ) );
		}


		public override string ToString()
		{
			return stringBuilder.ToString();
		}
	}
}
