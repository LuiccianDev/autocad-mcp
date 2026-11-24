namespace AutoCAD.Gateway.Contracts;

/// <summary>
/// Pure lexical validator for AutoLISP expressions sent through the LISP bridge.
/// </summary>
public static class LispSyntax
{
    /// <summary>
    /// Validates that an AutoLISP expression is non-empty, contains no NUL bytes,
    /// and has balanced parentheses, strings, and inline block comments.
    /// </summary>
    /// <param name="code">AutoLISP source text to validate.</param>
    /// <returns><c>null</c> when valid; otherwise a concise diagnostic error message.</returns>
    public static string? Validate(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return "LISP expression must not be empty.";
        }

        if (code.Contains('\0'))
        {
            return "LISP expression must not contain NUL bytes.";
        }

        int depth = 0;
        bool inString = false;
        bool escaped = false;
        bool inLineComment = false;
        bool inBlockComment = false;

        for (int i = 0; i < code.Length; i++)
        {
            char c = code[i];

            if (inLineComment)
            {
                if (c == '\r' || c == '\n')
                {
                    inLineComment = false;
                }
                continue;
            }

            if (inBlockComment)
            {
                if (c == '|' && i + 1 < code.Length && code[i + 1] == ';')
                {
                    inBlockComment = false;
                    i++;
                }
                continue;
            }

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }
                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == ';')
            {
                if (i + 1 < code.Length && code[i + 1] == '|')
                {
                    inBlockComment = true;
                    i++;
                }
                else
                {
                    inLineComment = true;
                }
                continue;
            }

            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth--;
                if (depth < 0)
                {
                    return "Unmatched closing parenthesis in LISP expression.";
                }
            }
        }

        if (inBlockComment)
        {
            return "Unclosed block comment ';|' in LISP expression.";
        }

        if (inString)
        {
            return "Unclosed string literal in LISP expression.";
        }

        if (depth != 0)
        {
            return "Unclosed opening parenthesis '(' in LISP expression.";
        }

        return null;
    }
}
