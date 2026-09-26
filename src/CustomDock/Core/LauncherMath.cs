using System.Globalization;

namespace CustomDock.Core;

/// <summary>
/// Quick math for the launcher: + - * / ^, parentheses, percent ("200*15%"), sqrt and pi. Both "." and "," work as the
/// decimal separator, so "3,5*2" and "3.5*2" both give 7.
/// </summary>
public static class LauncherMath
{
    /// <summary>Evaluates an expression; false when the text isn't a calculation.</summary>
    public static bool TryEvaluate(string text, out double result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string input = text.Trim().TrimStart('=').Replace('×', '*').Replace('÷', '/').Replace(" ", "");
        // A bare number or a word is not a calculation worth showing.
        if (!input.Any(char.IsDigit) || !input.Any(c => "+-*/^%(".Contains(c) || char.IsLetter(c))) return false;
        if (input.Length > 200) return false;
        try
        {
            var parser = new Parser(input);
            result = parser.ParseExpression();
            return parser.AtEnd && !double.IsNaN(result) && !double.IsInfinity(result);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string Format(double value)
    {
        if (Math.Abs(value) >= 1e15 || (Math.Abs(value) < 1e-6 && value != 0))
            return value.ToString("G10", CultureInfo.CurrentCulture);
        return Math.Round(value, 10).ToString("#,0.##########", CultureInfo.CurrentCulture);
    }

    private sealed class Parser
    {
        private readonly string _s;
        private int _i;

        public Parser(string s) => _s = s;

        public bool AtEnd => _i >= _s.Length;

        private char Peek => _i < _s.Length ? _s[_i] : '\0';

        public double ParseExpression()
        {
            double value = ParseTerm();
            while (Peek is '+' or '-')
            {
                char op = _s[_i++];
                double right = ParseTerm();
                value = op == '+' ? value + right : value - right;
            }
            return value;
        }

        private double ParseTerm()
        {
            double value = ParseFactor();
            while (Peek is '*' or '/' or 'x' or 'X')
            {
                char op = _s[_i++];
                double right = ParseFactor();
                if (op == '/')
                {
                    if (right == 0) throw new FormatException();
                    value /= right;
                }
                else
                {
                    value *= right;
                }
            }
            return value;
        }

        private double ParseFactor()
        {
            double value = ParseUnary();
            if (Peek == '^')
            {
                _i++;
                value = Math.Pow(value, ParseFactor()); // right-associative
            }
            return value;
        }

        private double ParseUnary()
        {
            if (Peek == '-') { _i++; return -ParseUnary(); }
            if (Peek == '+') { _i++; return ParseUnary(); }
            double value = ParsePrimary();
            while (Peek == '%') { _i++; value /= 100; }
            return value;
        }

        private double ParsePrimary()
        {
            if (Peek == '(')
            {
                _i++;
                double inner = ParseExpression();
                Expect(')');
                return inner;
            }
            if (char.IsLetter(Peek))
            {
                int start = _i;
                while (char.IsLetter(Peek)) _i++;
                string name = _s[start.._i].ToLowerInvariant();
                switch (name)
                {
                    case "pi":
                        return Math.PI;
                    case "sqrt":
                    case "karekök":
                        Expect('(');
                        double arg = ParseExpression();
                        Expect(')');
                        if (arg < 0) throw new FormatException();
                        return Math.Sqrt(arg);
                    default:
                        throw new FormatException();
                }
            }
            return ParseNumber();
        }

        private double ParseNumber()
        {
            int start = _i;
            while (char.IsDigit(Peek) || Peek is '.' or ',') _i++;
            if (start == _i) throw new FormatException();
            string number = _s[start.._i].Replace(',', '.');
            if (number.Count(c => c == '.') > 1) throw new FormatException();
            return double.Parse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        }

        private void Expect(char c)
        {
            if (Peek != c) throw new FormatException();
            _i++;
        }
    }
}

/// <summary>How well a launcher entry matches what was typed (0 = not at all).</summary>
public static class LauncherMatch
{
    private static readonly CompareInfo Compare = CultureInfo.CurrentCulture.CompareInfo;
    private const CompareOptions Options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    public static int Score(string title, string? keywords, string query)
    {
        if (query.Length == 0) return 1;
        if (Compare.IsPrefix(title, query, Options)) return 100;
        int index = Compare.IndexOf(title, query, Options);
        if (index > 0)
        {
            char before = title[index - 1];
            return char.IsWhiteSpace(before) || before is '-' or '_' or '(' or '.' ? 80 : 60;
        }
        if (index == 0) return 100;
        if (!string.IsNullOrEmpty(keywords) && Compare.IndexOf(keywords, query, Options) >= 0) return 45;
        return IsSubsequence(title, query) ? 30 : 0;
    }

    /// <summary>"vsc" matches "Visual Studio Code": every typed letter appears in order.</summary>
    private static bool IsSubsequence(string title, string query)
    {
        if (query.Length < 2) return false;
        int t = 0;
        foreach (char q in query)
        {
            if (char.IsWhiteSpace(q)) continue;
            bool found = false;
            while (t < title.Length)
            {
                if (Compare.Compare(title[t++].ToString(), q.ToString(), Options) == 0)
                {
                    found = true;
                    break;
                }
            }
            if (!found) return false;
        }
        return true;
    }
}
