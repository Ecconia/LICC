﻿using LICC.Internal.LSF.Parsing.Data;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LICC.Internal.LSF.Parsing
{
    internal class Lexer
    {
        private static readonly string[] Keywords = { "function", "true", "false", "null", "return", "if", "else", "for", "from", "to", "while" };

        private int Column;
        private int Index;
        private char Char => Source[Index];

        private int Line
        {
            get
            {
                int i;

                for (i = 0; i < NewlineIndices.Count; i++)
                {
                    if (Index < NewlineIndices[i])
                        break;
                }

                return i - 1;
            }
        }
        private SourceLocation Location => new SourceLocation(Line, Column);

        private readonly StringBuilder Buffer = new StringBuilder();
        private readonly List<int> NewlineIndices = new List<int>();

        private bool IsEOF => Char == '\0';
        private bool IsNewLine => Char == '\n';
        private bool IsSymbol => "{}()<>+-*/;#,!$=&|@?:".Contains(Char);
        private bool IsWhitespace => Char == ' ' || Char == '\t';
        private bool IsKeyword => Keywords.Contains(Buffer.ToString());

        public ErrorSink Errors { get; } = new ErrorSink();

        private readonly string Source;

        public Lexer(string source)
        {
            this.Source = source.Replace("\r\n", "\n");

            for (int i = 0; i < Source.Length; i++)
            {
                if (Source[i] == '\n')
                    NewlineIndices.Add(i);
            }

            if (this.Source[this.Source.Length - 1] != '\0')
                this.Source += "\0";
        }

        public IEnumerable<Lexeme> Lex()
        {
            while (!IsEOF)
            {
                var lexeme = GetLexeme();

                if (lexeme != null)
                    yield return lexeme;
            }

            yield return Lexeme(LexemeKind.EndOfFile);
        }

        public static IEnumerable<Lexeme> Lex(string source)
        {
            return new Lexer(source).Lex();
        }

        private void Advance()
        {
            Index++;
            Column++;
        }

        private void Back()
        {
            Index--;
            Column--;

            if (Column < 0)
            {
                Column = 0;
            }
        }

        private char Consume()
        {
            Buffer.Append(Char);
            return Take();
        }

        private char Take()
        {
            char c = Char;
            Advance();
            return c;
        }

        private void Error(string msg, Severity severity)
        {
            var error = new Error(Location, msg, severity);
            Errors.Add(error);

            if (severity == Severity.Error)
                throw new ParseException(error);
        }

        private Lexeme Lexeme(LexemeKind kind, string content = null)
        {
            content = content ?? Buffer.ToString();
            Buffer.Clear();

            var begin = Location;
            var end = new SourceLocation(Line, Column + content.Length);

            return new Lexeme(kind, begin, end, content);
        }

        private Lexeme GetLexeme()
        {
            if (IsWhitespace)
            {
                return DoWhitespace();
            }
            if (IsSymbol)
            {
                return DoSymbol();
            }
            else if (IsNewLine)
            {
                Advance();
                return Lexeme(LexemeKind.NewLine);
            }
            else
            {
                return DoString();
            }
        }

        private Lexeme DoWhitespace()
        {
            while (IsWhitespace)
                Consume();

            return Lexeme(LexemeKind.Whitespace);
        }

        private Lexeme DoSymbol()
        {
            switch (Take())
            {
                case '(':
                    return Lexeme(LexemeKind.LeftParenthesis);
                case ')':
                    return Lexeme(LexemeKind.RightParenthesis);
                case '{':
                    return Lexeme(LexemeKind.LeftBrace);
                case '}':
                    return Lexeme(LexemeKind.RightBrace);
                case '+':
                    return Lexeme(LexemeKind.Plus);
                case '-':
                    return Lexeme(LexemeKind.Minus);
                case '*':
                    return Lexeme(LexemeKind.Multiply);
                case '/':
                    return Lexeme(LexemeKind.Divide);
                case ';':
                    return Lexeme(LexemeKind.Semicolon);
                case '#':
                    return Lexeme(LexemeKind.Hashtag);
                case ',':
                    return Lexeme(LexemeKind.Comma);
                case '$':
                    return Lexeme(LexemeKind.Dollar);
                case '@':
                    return Lexeme(LexemeKind.AtSign);
                case '?':
                    return Lexeme(LexemeKind.QuestionMark);
                case ':':
                    return Lexeme(LexemeKind.Colon);
                case '&':
                    return TwoCharOperator('&', LexemeKind.And, LexemeKind.AndAlso);
                case '|':
                    return TwoCharOperator('|', LexemeKind.Or, LexemeKind.OrElse);
                case '!':
                    return TwoCharOperator('=', LexemeKind.Exclamation, LexemeKind.NotEqual);
                case '=':
                    return TwoCharOperator('=', LexemeKind.EqualsAssign, LexemeKind.Equals);
                case '<':
                    return TwoCharOperator('=', LexemeKind.Less, LexemeKind.LessOrEqual);
                case '>':
                    return TwoCharOperator('=', LexemeKind.More, LexemeKind.MoreOrEqual);
            }

            return null;

            Lexeme TwoCharOperator(char second, LexemeKind firstKind, LexemeKind secondKind)
            {
                if (Consume() == second)
                    return Lexeme(secondKind);

                Back();
                return Lexeme(firstKind);
            }
        }

        private Lexeme DoString()
        {
            char quote = '\0';
            bool isQuoted = false;

            if (Char == '"' || Char == '\'')
            {
                quote = Char;
                isQuoted = true;
                Advance();
            }

            while (!IsEOF && !IsNewLine && (isQuoted ? Char != quote : (!IsWhitespace && !IsSymbol)))
            {
                if (Consume() == '\\' && !IsEOF)
                    Consume();
            }

            if (isQuoted)
            {
                if (IsEOF || Char != quote)
                    Error("missing closing quote", Severity.Error);

                Advance();
            }

            return Lexeme(isQuoted
                ? LexemeKind.QuotedString
                : (IsKeyword
                    ? LexemeKind.Keyword
                    : LexemeKind.String));
        }
    }
}
