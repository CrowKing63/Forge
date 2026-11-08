using System.Text;

namespace Forge.Core;

/// <summary>
/// A renderer for the Jinja2 subset used by LLM chat templates embedded in GGUF files.
///
/// The llama.cpp C API (<c>llama_chat_apply_template</c>) does not interpret Jinja — it only
/// supports a pre-defined list of template patterns (S21 finding). Gemma 4's 18 KB macro-heavy
/// template is not in that list, so Forge renders the embedded template itself with this
/// interpreter. The supported subset covers what LLM chat templates actually use:
/// <c>{{ expr }}</c>, <c>{% if/elif/else %}</c>, <c>{% for %}</c>, <c>{% set %}</c>,
/// <c>{% macro %}</c>, <c>{% generation %}</c>, <c>is</c> type tests, filters (<c>default</c>,
/// <c>tojson</c>, <c>trim</c>, <c>join</c>, <c>length</c>, <c>upper</c>, <c>lower</c>,
/// <c>string</c>, <c>bool</c>, <c>replace</c>), method calls (<c>.get()</c>, <c>.split()</c>,
/// <c>.endswith()</c>, <c>.items()</c>), slice notation, <c>namespace()</c>, and
/// whitespace control (<c>{%- -%}</c>).
///
/// Verification: for each catalog model, the rendered prompt is byte-compared against the
/// reference output of Python's <c>jinja2</c> library (the standard implementation) in the
/// test suite.
/// </summary>
internal sealed class JinjaRenderer
{
    /// <summary>
    /// Renders <paramref name="template"/> with the given context variables.
    /// Throws <see cref="JinjaRenderException"/> on a template syntax error or an
    /// unsupported construct — callers surface this as "model not certified".
    /// </summary>
    public static string Render(string template, IReadOnlyDictionary<string, object?> context)
    {
        var tokens = Lexer.Tokenize(template);
        var parser = new Parser(tokens);
        var root = parser.ParseBlock(null);
        var evaluator = new Evaluator(context);
        return evaluator.Evaluate(root);
    }

    /// <summary>Thrown when a template uses a construct this renderer does not support.</summary>
    public sealed class JinjaRenderException : Exception
    {
        public JinjaRenderException(string message) : base(message) { }
    }

    // ========================================================================
    // Lexer — flat token stream with classified statement tags
    // ========================================================================

    internal enum TagKind
    {
        Text,
        Output,   // {{ expr }}
        Set,      // {% set x = ... %}
        EndSet,   // {% endset %} (block set)
        If,       // {% if cond %}
        Elif,     // {% elif cond %}
        Else,     // {% else %}
        EndIf,    // {% endif %}
        For,      // {% for x in seq %}
        EndFor,   // {% endfor %}
        Macro,    // {% macro name(a, b) %}
        EndMacro, // {% endmacro %}
        Generation,  // {% generation %} (no-op marker)
        EndGeneration, // {% endgeneration %} (no-op marker)
    }

    internal sealed record Token(TagKind Kind, string Body, int Pos)
    {
        public override string ToString() => $"{Kind}:{Body}";
    }

    internal static class Lexer
    {
        public static List<Token> Tokenize(string src)
        {
            var tokens = new List<Token>();
            int textStart = 0;

            while (true)
            {
                int openOut = src.IndexOf("{{", textStart, StringComparison.Ordinal);
                int openStmt = src.IndexOf("{%", textStart, StringComparison.Ordinal);
                int next;
                if (openOut < 0) next = openStmt;
                else if (openStmt < 0) next = openOut;
                else next = Math.Min(openOut, openStmt);
                if (next < 0) break;

                bool isOutput = openStmt < 0 || (openOut >= 0 && openOut <= openStmt);
                string closeMarker = isOutput ? "}}" : "%}";

                // Whitespace control: {{- / {%- strips preceding whitespace.
                bool dashOpen = next + 2 < src.Length && src[next + 2] == '-';
                int bodyStart = next + (dashOpen ? 3 : 2);

                if (dashOpen)
                    textStart = TrimTrailingWs(src, textStart, next);

                if (next > textStart)
                    tokens.Add(new Token(TagKind.Text, src[textStart..next], textStart));

                int close = src.IndexOf(closeMarker, bodyStart, StringComparison.Ordinal);
                if (close < 0)
                    throw new JinjaRenderException($"Unclosed tag at position {next}.");

                // Whitespace control: -}} / -%} strips following whitespace.
                // The dash is the character immediately before the closing marker (}} or %}).
                bool dashClose = close > bodyStart && src[close - 1] == '-';
                int bodyEnd = dashClose ? close - 1 : close;
                string body = src[bodyStart..bodyEnd];

                TagKind kind = Classify(isOutput, body);
                tokens.Add(new Token(kind, body, next));

                int afterTag = close + 2; // tag always ends with the 2-char closing marker
                if (dashClose)
                    afterTag = TrimLeadingWs(src, afterTag, src.Length);
                textStart = afterTag;
            }

            if (textStart < src.Length)
                tokens.Add(new Token(TagKind.Text, src[textStart..], textStart));

            return tokens;
        }

        private static TagKind Classify(bool isOutput, string body)
        {
            if (isOutput) return TagKind.Output;

            var trimmed = body.Trim();
            if (trimmed.StartsWith("set ", StringComparison.Ordinal)) return TagKind.Set;
            if (trimmed == "endset") return TagKind.EndSet;
            if (trimmed.StartsWith("if ", StringComparison.Ordinal)) return TagKind.If;
            if (trimmed.StartsWith("elif ", StringComparison.Ordinal)) return TagKind.Elif;
            if (trimmed == "else") return TagKind.Else;
            if (trimmed == "endif") return TagKind.EndIf;
            if (trimmed.StartsWith("for ", StringComparison.Ordinal)) return TagKind.For;
            if (trimmed == "endfor") return TagKind.EndFor;
            if (trimmed.StartsWith("macro ", StringComparison.Ordinal)) return TagKind.Macro;
            if (trimmed == "endmacro") return TagKind.EndMacro;
            if (trimmed == "generation") return TagKind.Generation;
            if (trimmed == "endgeneration") return TagKind.EndGeneration;

            throw new JinjaRenderException($"Unsupported statement: {body}");
        }

        private static int TrimTrailingWs(string src, int start, int end)
        {
            int i = end - 1;
            while (i >= start && char.IsWhiteSpace(src[i])) i--;
            return i + 1;
        }

        private static int TrimLeadingWs(string src, int start, int end)
        {
            int i = start;
            while (i < end && char.IsWhiteSpace(src[i])) i++;
            return i;
        }
    }

    // ========================================================================
    // AST
    // ========================================================================

    internal abstract record AstNode;

    internal sealed record TextNode(string Value) : AstNode;
    internal sealed record OutputNode(ExprNode Expr) : AstNode;
    internal sealed record SetNode(string Name, ExprNode Value) : AstNode;
    internal sealed record BlockSetNode(string Name, List<AstNode> Body) : AstNode;
    internal sealed record IfNode(List<(ExprNode Cond, List<AstNode> Body)> Branches, List<AstNode>? ElseBody) : AstNode;
    internal sealed record ForNode(string VarName, ExprNode Iterable, List<AstNode> Body) : AstNode;
    internal sealed record MacroDefNode(string Name, List<string> ParamNames, List<AstNode> Body) : AstNode;
    internal sealed record NoOpNode : AstNode;

    // Expression nodes.
    internal abstract record ExprNode;
    internal sealed record LiteralNode(object? Value) : ExprNode;
    internal sealed record VarNode(string Name) : ExprNode;
    internal sealed record AttrNode(ExprNode Target, string Name) : ExprNode;
    internal sealed record IndexNode(ExprNode Target, ExprNode Index) : ExprNode;
    internal sealed record SliceNode(ExprNode Target, ExprNode? Lower, ExprNode? Upper) : ExprNode;
    internal sealed record CompareNode(ExprNode Left, string Op, ExprNode Right) : ExprNode;
    internal sealed record ArithNode(string Op, ExprNode Left, ExprNode Right) : ExprNode;
    internal sealed record BoolOpNode(string Op, List<ExprNode> Values) : ExprNode;
    internal sealed record NotNode(ExprNode Value) : ExprNode;
    internal sealed record InNode(ExprNode Value, ExprNode Sequence) : ExprNode;
    internal sealed record IsNode(ExprNode Value, string Test, bool Negated) : ExprNode;
    internal sealed record FilterNode(ExprNode Input, string Name, List<ExprNode> Args) : ExprNode;
    internal sealed record CallNode(ExprNode Target, List<ExprNode> Args) : ExprNode;
    internal sealed record KeywordArgNode(string Name, ExprNode Value) : ExprNode;
    internal sealed record ConditionalNode(ExprNode IfTrue, ExprNode Cond, ExprNode IfFalse) : ExprNode;
    internal sealed record MethodCallNode(ExprNode Target, string Method, List<ExprNode> Args) : ExprNode;

    // ========================================================================
    // Parser — structural (handles nested if/for/macro blocks)
    // ========================================================================

    internal sealed class Parser
    {
        private readonly List<Token> _tokens;
        private int _pos;

        public Parser(List<Token> tokens)
        {
            _tokens = tokens;
            _pos = 0;
        }

        /// <summary>
        /// Parses a block of nodes, stopping (without consuming) when the next token
        /// is one of <paramref name="stopKinds"/>. Returns the parsed nodes.
        /// </summary>
        public List<AstNode> ParseBlock(TagKind[]? stopKinds)
        {
            var nodes = new List<AstNode>();

            while (_pos < _tokens.Count)
            {
                var t = _tokens[_pos];

                if (stopKinds is not null && Array.IndexOf(stopKinds, t.Kind) >= 0)
                    break;

                switch (t.Kind)
                {
                    case TagKind.Text:
                        nodes.Add(new TextNode(t.Body));
                        _pos++;
                        break;

                    case TagKind.Output:
                        nodes.Add(new OutputNode(ParseExpr(t.Body)));
                        _pos++;
                        break;

                    case TagKind.Generation:
                    case TagKind.EndGeneration:
                        nodes.Add(new NoOpNode());
                        _pos++;
                        break;

                    case TagKind.Set:
                    {
                        int eq = FindTopLevelChar(t.Body, '=');
                        if (eq < 0)
                        {
                            // Block set: {% set x %}...content...{% endset %}
                            string name = t.Body.Trim();
                            _pos++; // consume {% set %}
                            var body = ParseBlock(new[] { TagKind.EndSet });
                            Expect(TagKind.EndSet);
                            nodes.Add(new BlockSetNode(name, body));
                        }
                        else
                        {
                            string name = t.Body[..eq].Trim();
                            nodes.Add(new SetNode(name, ParseExpr(t.Body[(eq + 1)..])));
                            _pos++;
                        }
                        break;
                    }

                    case TagKind.Macro:
                    {
                        string macroBody = t.Body["macro ".Length..].Trim();
                        int openParen = macroBody.IndexOf('(', StringComparison.Ordinal);
                        if (openParen < 0)
                            throw new JinjaRenderException($"Malformed macro: {t.Body}");
                        string name = macroBody[..openParen].Trim();
                        int closeParen = macroBody.IndexOf(")", openParen, StringComparison.Ordinal);
                        if (closeParen < 0)
                            throw new JinjaRenderException($"Malformed macro: {t.Body}");
                        var paramNames = macroBody[(openParen + 1)..closeParen]
                            .Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(p => p.Trim()).ToList();

                        _pos++; // consume {% macro %}
                        var body = ParseBlock(new[] { TagKind.EndMacro });
                        Expect(TagKind.EndMacro);
                        nodes.Add(new MacroDefNode(name, paramNames, body));
                        break;
                    }

                    case TagKind.If:
                    {
                        var branches = new List<(ExprNode, List<AstNode>)>();
                        string condSrc = t.Body["if ".Length..].Trim();
                        _pos++; // consume {% if %} before parsing the body
                        branches.Add((ParseExpr(condSrc), ParseBlock(new[] { TagKind.Elif, TagKind.Else, TagKind.EndIf })));

                        while (Peek() is { Kind: TagKind.Elif })
                        {
                            string elifCond = _tokens[_pos].Body["elif ".Length..].Trim();
                            _pos++; // consume {% elif %} before parsing the body
                            branches.Add((ParseExpr(elifCond), ParseBlock(new[] { TagKind.Elif, TagKind.Else, TagKind.EndIf })));
                        }

                        List<AstNode>? elseBody = null;
                        if (Peek() is { Kind: TagKind.Else })
                        {
                            _pos++; // consume {% else %}
                            elseBody = ParseBlock(new[] { TagKind.EndIf });
                        }

                        Expect(TagKind.EndIf);
                        nodes.Add(new IfNode(branches, elseBody));
                        break;
                    }

                    case TagKind.For:
                    {
                        int inIdx = t.Body.IndexOf(" in ", StringComparison.Ordinal);
                        if (inIdx < 0)
                            throw new JinjaRenderException($"Malformed for: {t.Body}");
                        string varName = t.Body["for ".Length..inIdx].Trim();
                        var iterable = ParseExpr(t.Body[(inIdx + 4)..].Trim());

                        _pos++; // consume {% for %}
                        var body = ParseBlock(new[] { TagKind.EndFor });
                        Expect(TagKind.EndFor);
                        nodes.Add(new ForNode(varName, iterable, body));
                        break;
                    }

                    default:
                        throw new JinjaRenderException($"Unexpected token in block: {t}");
                }
            }

            return nodes;
        }

        private Token? Peek() => _pos < _tokens.Count ? _tokens[_pos] : null;

        private void Expect(TagKind kind)
        {
            var t = Peek();
            if (t is null || t.Kind != kind)
                throw new JinjaRenderException($"Expected {{% {kind} %}} but found {t}.");
            _pos++;
        }

        private static int FindTopLevelChar(string src, char c)
        {
            int depth = 0;
            for (int i = 0; i < src.Length; i++)
            {
                if (src[i] == '(' || src[i] == '[') depth++;
                else if (src[i] == ')' || src[i] == ']') depth--;
                else if (src[i] == c && depth == 0) return i;
            }
            return -1;
        }

        // ---- expression parsing (recursive descent) ----

        public static ExprNode ParseExpr(string src) => new ExprParser(src.Trim()).ParseComparison();

        private sealed class ExprParser
        {
            private readonly string _src;
            private int _pos;

            public ExprParser(string src) { _src = src; _pos = 0; }

            public ExprNode ParseComparison()
            {
                var left = ParseBoolOr();
                while (_pos < _src.Length)
                {
                    SkipWs();
                    if (TryRead("==")) return new CompareNode(left, "==", ParseComparison());
                    if (TryRead("!=")) return new CompareNode(left, "!=", ParseComparison());
                    if (TryRead("<=")) return new CompareNode(left, "<=", ParseComparison());
                    if (TryRead(">=")) return new CompareNode(left, ">=", ParseComparison());
                    if (TryRead("<")) return new CompareNode(left, "<", ParseComparison());
                    if (TryRead(">")) return new CompareNode(left, ">", ParseComparison());
                    break;
                }
                // Conditional expression: A if B [else C]
                SkipWs();
                if (TryReadWord("if"))
                {
                    var cond = ParseBoolOr();
                    SkipWs();
                    ExprNode elseVal;
                    if (TryReadWord("else"))
                        elseVal = ParseComparison();
                    else
                        elseVal = new LiteralNode(null);
                    return new ConditionalNode(left, cond, elseVal);
                }
                return left;
            }

            public ExprNode ParseBoolOr()
            {
                var left = ParseBoolAnd();
                while (_pos < _src.Length)
                {
                    SkipWs();
                    if (TryReadWord("or"))
                        left = new BoolOpNode("or", new() { left, ParseBoolAnd() });
                    else break;
                }
                return left;
            }

            public ExprNode ParseBoolAnd()
            {
                var left = ParseNot();
                while (_pos < _src.Length)
                {
                    SkipWs();
                    if (TryReadWord("and"))
                        left = new BoolOpNode("and", new() { left, ParseNot() });
                    else break;
                }
                return left;
            }

            public ExprNode ParseNot()
            {
                SkipWs();
                if (TryReadWord("not"))
                    return new NotNode(ParseNot());
                return ParseIn();
            }

            public ExprNode ParseIn()
            {
                var left = ParseAdditive();
                SkipWs();
                if (TryReadWord("in"))
                    return new InNode(left, ParseAdditive());
                if (TryReadWord("is"))
                {
                    SkipWs();
                    bool negated = TryReadWord("not");
                    string test = ReadName();
                    return new IsNode(left, test, negated);
                }
                return left;
            }

            public ExprNode ParseAdditive()
            {
                var left = ParseMultiplicative();
                while (_pos < _src.Length)
                {
                    SkipWs();
                    if (TryRead("+")) left = new ArithNode("+", left, ParseMultiplicative());
                    else if (TryRead("-")) left = new ArithNode("-", left, ParseMultiplicative());
                    else break;
                }
                return left;
            }

            public ExprNode ParseMultiplicative()
            {
                var left = ParseUnary();
                while (_pos < _src.Length)
                {
                    SkipWs();
                    if (TryRead("*")) left = new ArithNode("*", left, ParseUnary());
                    else if (TryRead("/")) left = new ArithNode("/", left, ParseUnary());
                    else if (TryRead("%")) left = new ArithNode("%", left, ParseUnary());
                    else break;
                }
                return left;
            }

            public ExprNode ParseUnary()
            {
                SkipWs();
                if (_pos < _src.Length && _src[_pos] == '-' && _pos + 1 < _src.Length &&
                    (char.IsDigit(_src[_pos + 1]) || char.IsLetter(_src[_pos + 1])))
                {
                    _pos++; // consume the '-' before recursing
                    return new ArithNode("-", new LiteralNode(0L), ParseUnary());
                }
                return ParseFilter();
            }

            public ExprNode ParseFilter()
            {
                var expr = ParsePrimary();
                while (_pos < _src.Length)
                {
                    SkipWs();
                    if (_src[_pos] == '|')
                    {
                        _pos++;
                        string name = ReadName();
                        var args = new List<ExprNode>();
                        SkipWs();
                        if (_pos < _src.Length && _src[_pos] == '(')
                        {
                            _pos++;
                            while (_pos < _src.Length && _src[_pos] != ')')
                            {
                                SkipWs();
                                args.Add(ParseComparison());
                                SkipWs();
                                if (_pos < _src.Length && _src[_pos] == ',') _pos++;
                                else break;
                            }
                            if (_pos < _src.Length) _pos++; // )
                        }
                        expr = new FilterNode(expr, name, args);
                    }
                    else break;
                }
                return expr;
            }

            public ExprNode ParsePrimary()
            {
                SkipWs();
                if (_pos >= _src.Length)
                    throw new JinjaRenderException("Unexpected end of expression.");

                char c = _src[_pos];

                if (c == '(')
                {
                    _pos++;
                    var e = ParseComparison();
                    SkipWs();
                    if (_pos < _src.Length && _src[_pos] == ')') _pos++;
                    return e;
                }

                // List literal: [expr, expr, ...]
                if (c == '[')
                {
                    _pos++;
                    var items = new List<ExprNode>();
                    SkipWs();
                    if (_pos < _src.Length && _src[_pos] == ']')
                    {
                        _pos++;
                        return new LiteralNode(items);
                    }
                    while (_pos < _src.Length)
                    {
                        items.Add(ParseComparison());
                        SkipWs();
                        if (_pos < _src.Length && _src[_pos] == ',') { _pos++; continue; }
                        if (_pos < _src.Length && _src[_pos] == ']') { _pos++; break; }
                        break;
                    }
                    return new LiteralNode(items);
                }

                if (c == '\'' || c == '"')
                    return new LiteralNode(ReadString());

                if (char.IsDigit(c) || (c == '-' && _pos + 1 < _src.Length && char.IsDigit(_src[_pos + 1])))
                    return new LiteralNode(ReadNumber());

                string name = ReadName();
                switch (name)
                {
                    case "true": return new LiteralNode(true);
                    case "false": return new LiteralNode(false);
                    case "none":
                    case "null": return new LiteralNode(null);
                }

                // Function call?
                SkipWs();
                if (_pos < _src.Length && _src[_pos] == '(')
                {
                    _pos++;
                    var args = new List<ExprNode>();
                    while (_pos < _src.Length && _src[_pos] != ')')
                    {
                        SkipWs();
                        // Keyword argument: name=value
                        if (char.IsLetter(_src[_pos]))
                        {
                            int nameStart = _pos;
                            while (_pos < _src.Length && (char.IsLetterOrDigit(_src[_pos]) || _src[_pos] == '_'))
                                _pos++;
                            string kwName = _src[nameStart.._pos];
                            SkipWs();
                            if (_pos < _src.Length && _src[_pos] == '=' &&
                                (_pos + 1 >= _src.Length || _src[_pos + 1] != '='))
                            {
                                _pos++; // consume single =
                                args.Add(new KeywordArgNode(kwName, ParseComparison()));
                            }
                            else
                            {
                                // Not a keyword arg — parse as normal expression.
                                _pos = nameStart;
                                args.Add(ParseComparison());
                            }
                        }
                        else
                        {
                            args.Add(ParseComparison());
                        }
                        SkipWs();
                        if (_pos < _src.Length && _src[_pos] == ',') _pos++;
                        else break;
                    }
                    if (_pos < _src.Length) _pos++; // )
                    return new CallNode(new VarNode(name), args);
                }

                var node = (ExprNode)new VarNode(name);

                // Attribute / index / method access.
                while (_pos < _src.Length)
                {
                    SkipWs();
                    if (_pos >= _src.Length) break;
                    if (_src[_pos] == '.')
                    {
                        _pos++;
                        string field = ReadName();
                        // Check for method call: .name(
                        SkipWs();
                        if (_pos < _src.Length && _src[_pos] == '(')
                        {
                            _pos++;
                            var mArgs = new List<ExprNode>();
                            while (_pos < _src.Length && _src[_pos] != ')')
                            {
                                SkipWs();
                                mArgs.Add(ParseComparison());
                                SkipWs();
                                if (_pos < _src.Length && _src[_pos] == ',') _pos++;
                                else break;
                            }
                            if (_pos < _src.Length) _pos++; // )
                            node = new MethodCallNode(node, field, mArgs);
                        }
                        else
                        {
                            node = new AttrNode(node, field);
                        }
                    }
                    else if (_src[_pos] == '[')
                    {
                        _pos++;
                        SkipWs();
                        // Check for slice: [:N], [N:], [A:B]
                        if (_pos < _src.Length && _src[_pos] == ':')
                        {
                            // [:N] or [:]
                            _pos++;
                            SkipWs();
                            ExprNode? upper;
                            if (_pos < _src.Length && _src[_pos] != ']')
                                upper = ParseComparison();
                            else
                                upper = null;
                            SkipWs();
                            if (_pos < _src.Length && _src[_pos] == ']') _pos++;
                            node = new SliceNode(node, null, upper);
                        }
                        else
                        {
                            var lower = ParseComparison();
                            SkipWs();
                            if (_pos < _src.Length && _src[_pos] == ':')
                            {
                                // [N:] or [A:B]
                                _pos++;
                                SkipWs();
                                ExprNode? upper;
                                if (_pos < _src.Length && _src[_pos] != ']')
                                    upper = ParseComparison();
                                else
                                    upper = null;
                                SkipWs();
                                if (_pos < _src.Length && _src[_pos] == ']') _pos++;
                                node = new SliceNode(node, lower, upper);
                            }
                            else
                            {
                                if (_pos < _src.Length && _src[_pos] == ']') _pos++;
                                node = new IndexNode(node, lower);
                            }
                        }
                    }
                    else break;
                }

                return node;
            }

            private void SkipWs()
            {
                while (_pos < _src.Length && char.IsWhiteSpace(_src[_pos])) _pos++;
            }

            private bool TryRead(string s)
            {
                if (_src.AsSpan(_pos).StartsWith(s, StringComparison.Ordinal))
                {
                    _pos += s.Length;
                    return true;
                }
                return false;
            }

            private bool TryReadWord(string w)
            {
                SkipWs();
                if (_src.AsSpan(_pos).StartsWith(w, StringComparison.Ordinal) &&
                    (_pos + w.Length >= _src.Length || !char.IsLetterOrDigit(_src[_pos + w.Length])))
                {
                    _pos += w.Length;
                    return true;
                }
                return false;
            }

            private string ReadName()
            {
                SkipWs();
                int start = _pos;
                while (_pos < _src.Length && (char.IsLetterOrDigit(_src[_pos]) || _src[_pos] == '_'))
                    _pos++;
                if (_pos == start) throw new JinjaRenderException($"Expected name at position {start}.");
                return _src[start.._pos];
            }

            private string ReadString()
            {
                char quote = _src[_pos]; _pos++;
                var sb = new StringBuilder();
                while (_pos < _src.Length && _src[_pos] != quote)
                {
                    if (_src[_pos] == '\\' && _pos + 1 < _src.Length)
                    {
                        _pos++;
                        sb.Append(_src[_pos] switch
                        {
                            'n' => '\n',
                            't' => '\t',
                            '"' => '"',
                            '\'' => '\'',
                            '\\' => '\\',
                            _ => _src[_pos],
                        });
                    }
                    else
                    {
                        sb.Append(_src[_pos]);
                    }
                    _pos++;
                }
                if (_pos < _src.Length) _pos++; // closing quote
                return sb.ToString();
            }

            private object ReadNumber()
            {
                int start = _pos;
                if (_src[_pos] == '-') _pos++;
                while (_pos < _src.Length && char.IsDigit(_src[_pos])) _pos++;
                bool isFloat = false;
                if (_pos < _src.Length && _src[_pos] == '.')
                {
                    isFloat = true;
                    _pos++;
                    while (_pos < _src.Length && char.IsDigit(_src[_pos])) _pos++;
                }
                string numStr = _src[start.._pos];
                return isFloat ? (object)double.Parse(numStr, System.Globalization.CultureInfo.InvariantCulture)
                               : (object)long.Parse(numStr, System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }

    // ========================================================================
    // Evaluator
    // ========================================================================

    internal sealed class Evaluator
    {
        private readonly Dictionary<string, object?> _scope;
        private readonly Dictionary<string, MacroDefNode> _macros = new();

        public Evaluator(IReadOnlyDictionary<string, object?> context)
        {
            _scope = new Dictionary<string, object?>(context);
        }

        public string Evaluate(List<AstNode> nodes)
        {
            var sb = new StringBuilder();
            EvaluateNodes(nodes, sb);
            return sb.ToString();
        }

        private void EvaluateNodes(List<AstNode> nodes, StringBuilder sb)
        {
            foreach (var node in nodes)
                EvaluateNode(node, sb);
        }

        private void EvaluateNode(AstNode node, StringBuilder sb)
        {
            switch (node)
            {
                case TextNode t:
                    sb.Append(t.Value);
                    break;
                case OutputNode o:
                    sb.Append(ToString(EvalExpr(o.Expr)));
                    break;
                case SetNode s:
                {
                    if (s.Name.Contains('.'))
                    {
                        // Dotted assignment: ns.field = value
                        var parts = s.Name.Split('.');
                        object? target = _scope.TryGetValue(parts[0], out var t) ? t : null;
                        for (int i = 1; i < parts.Length - 1 && target is Dictionary<string, object?> d; i++)
                        {
                            if (!d.TryGetValue(parts[i], out var next))
                                throw new JinjaRenderException($"Cannot set '{s.Name}' — intermediate key '{parts[i]}' not found.");
                            target = next;
                        }
                        if (target is Dictionary<string, object?> td)
                            td[parts[^1]] = EvalExpr(s.Value);
                    }
                    else
                    {
                        _scope[s.Name] = EvalExpr(s.Value);
                    }
                    break;
                }
                case BlockSetNode bs:
                {
                    var sbInner = new StringBuilder();
                    EvaluateNodes(bs.Body, sbInner);
                    _scope[bs.Name] = sbInner.ToString();
                    break;
                }
                case MacroDefNode m:
                    _macros[m.Name] = m;
                    break;
                case NoOpNode:
                    break;
                case IfNode i:
                    foreach (var (cond, body) in i.Branches)
                    {
                        if (ToBool(EvalExpr(cond)))
                        {
                            EvaluateNodes(body, sb);
                            return;
                        }
                    }
                    if (i.ElseBody is not null)
                        EvaluateNodes(i.ElseBody, sb);
                    break;
                case ForNode f:
                    var items = EvalExpr(f.Iterable) as List<object?>;
                    if (items is not null)
                    {
                        for (int i = 0; i < items.Count; i++)
                        {
                            _scope[f.VarName] = items[i];
                            _scope["loop"] = new Dictionary<string, object?>
                            {
                                ["index"] = (long)(i + 1),
                                ["index0"] = (long)i,
                                ["first"] = i == 0,
                                ["last"] = i == items.Count - 1,
                                ["length"] = (long)items.Count,
                            };
                            EvaluateNodes(f.Body, sb);
                        }
                        _scope.Remove("loop");
                    }
                    break;
            }
        }

        private object? EvalExpr(ExprNode node)
        {
            switch (node)
            {
                case LiteralNode l:
                    return l.Value;
                case VarNode v:
                    return _scope.TryGetValue(v.Name, out var val) ? val : null;
                case AttrNode a:
                    if (EvalExpr(a.Target) is Dictionary<string, object?> dict && dict.TryGetValue(a.Name, out var av))
                        return av;
                    return null;
                case IndexNode ix:
                    var t = EvalExpr(ix.Target);
                    var idx = EvalExpr(ix.Index);
                    if (t is List<object?> lst && idx is long li && li >= 0 && li < lst.Count)
                        return lst[(int)li];
                    if (t is Dictionary<string, object?> d && idx is string s && d.TryGetValue(s, out var dv))
                        return dv;
                    // Negative indexing
                    if (t is List<object?> nlst && idx is long nli && nli < 0)
                    {
                        int adjusted = nlst.Count + (int)nli;
                        return adjusted >= 0 && adjusted < nlst.Count ? nlst[adjusted] : null;
                    }
                    if (t is string ns && idx is long nsi && nsi < 0)
                    {
                        int adj = ns.Length + (int)nsi;
                        return adj >= 0 && adj < ns.Length ? ns[adj].ToString() : null;
                    }
                    return null;
                case SliceNode sl:
                {
                    var target = EvalExpr(sl.Target);
                    long lo = sl.Lower == null ? 0 : (long)(EvalExpr(sl.Lower) is long lv ? lv : 0);
                    long hi = sl.Upper == null ? long.MaxValue : (long)(EvalExpr(sl.Upper) is long hv ? hv : long.MaxValue);
                    if (target is string str)
                    {
                        lo = Math.Max(0, lo);
                        hi = Math.Min(str.Length, hi);
                        return lo < hi ? str.Substring((int)lo, (int)(hi - lo)) : "";
                    }
                    if (target is List<object?> list)
                    {
                        lo = Math.Max(0, lo);
                        hi = Math.Min(list.Count, hi);
                        var result = new List<object?>();
                        for (int i = (int)lo; i < (int)hi; i++) result.Add(list[i]);
                        return result;
                    }
                    return target;
                }
                case CompareNode c:
                    return CompareOp(c.Left, c.Op, c.Right);
                case ArithNode ar:
                    return ArithOp(ar.Op, EvalExpr(ar.Left), EvalExpr(ar.Right));
                case BoolOpNode b:
                    if (b.Op == "and")
                    {
                        foreach (var v in b.Values)
                            if (!ToBool(EvalExpr(v))) return false;
                        return true;
                    }
                    foreach (var v in b.Values)
                        if (ToBool(EvalExpr(v))) return true;
                    return false;
                case NotNode n:
                    return !ToBool(EvalExpr(n.Value));
                case InNode inN:
                {
                    var value = EvalExpr(inN.Value);
                    var seq = EvalExpr(inN.Sequence);
                    if (seq is string ss && value is string vs)
                        return ss.Contains(vs, StringComparison.Ordinal);
                    if (seq is List<object?> list)
                        return list.Any(x => ValuesEqual(x, value));
                    if (seq is Dictionary<string, object?> d2 && value is string ks)
                        return d2.ContainsKey(ks);
                    return false;
                }
                case ConditionalNode cond:
                    return ToBool(EvalExpr(cond.Cond)) ? EvalExpr(cond.IfTrue) : EvalExpr(cond.IfFalse);
                case IsNode isN:
                    var iv = EvalExpr(isN.Value);
                    bool testResult = isN.Test switch
                    {
                        "string" => iv is string,
                        "boolean" or "bool" => iv is bool,
                        "none" or "null" => iv is null,
                        "mapping" => iv is Dictionary<string, object?>,
                        "sequence" or "iterable" => iv is List<object?> || iv is string,
                        "number" => iv is long or double,
                        "integer" or "int" => iv is long,
                        "float" => iv is double,
                        _ => throw new JinjaRenderException($"Unsupported test: {isN.Test}"),
                    };
                    return isN.Negated ? !testResult : testResult;
                case FilterNode f:
                    return ApplyFilter(f, EvalExpr(f.Input));
                case MethodCallNode mc:
                    return InvokeMethod(mc, EvalExpr(mc.Target));
                case CallNode call:
                    if (call.Target is VarNode cv)
                    {
                        if (cv.Name == "range")
                            return RangeCall(call.Args);
                        if (cv.Name == "namespace")
                            return NamespaceCall(call.Args);
                        if (cv.Name == "raise_exception")
                        {
                            string msg = call.Args.Count > 0 ? ToString(EvalExpr(call.Args[0])) : "";
                            throw new JinjaRenderException($"raise_exception: {msg}");
                        }
                        if (_macros.TryGetValue(cv.Name, out var macro))
                        {
                            var savedScope = new Dictionary<string, object?>(_scope);
                            int posIdx = 0;
                            foreach (var arg in call.Args)
                            {
                                if (arg is KeywordArgNode kw)
                                {
                                    _scope[kw.Name] = EvalExpr(kw.Value);
                                }
                                else if (posIdx < macro.ParamNames.Count)
                                {
                                    _scope[macro.ParamNames[posIdx]] = EvalExpr(arg);
                                    posIdx++;
                                }
                            }
                            string macroResult = Evaluate(macro.Body);
                            _scope.Clear();
                            foreach (var kv in savedScope) _scope[kv.Key] = kv.Value;
                            return macroResult;
                        }
                    }
                    throw new JinjaRenderException($"Unsupported call: {call.Target}");
            }
            throw new JinjaRenderException($"Unsupported expression node: {node.GetType().Name}");
        }

        private object? InvokeMethod(MethodCallNode mc, object? target)
        {
            switch (mc.Method)
            {
                case "get" when target is Dictionary<string, object?> dict:
                {
                    string key = mc.Args.Count > 0 ? ToString(EvalExpr(mc.Args[0])) : "";
                    return dict.TryGetValue(key, out var v) ? v : (mc.Args.Count > 1 ? EvalExpr(mc.Args[1]) : null);
                }
                case "split" when target is string s:
                {
                    string sep = mc.Args.Count > 0 ? ToString(EvalExpr(mc.Args[0])) : " ";
                    return s.Split(new[] { sep }, StringSplitOptions.None).Cast<object?>().ToList();
                }
                case "endswith" when target is string se:
                    return se.EndsWith(ToString(mc.Args.Count > 0 ? EvalExpr(mc.Args[0]) : null), StringComparison.Ordinal);
                case "startswith" when target is string ss:
                    return ss.StartsWith(ToString(mc.Args.Count > 0 ? EvalExpr(mc.Args[0]) : null), StringComparison.Ordinal);
                case "items" when target is Dictionary<string, object?> d:
                {
                    var list = new List<object?>();
                    foreach (var kv in d)
                        list.Add(new[] { (object?)kv.Key, kv.Value });
                    return list;
                }
                case "strip" when target is string st:
                    return st.Trim();
                case "join" when target is List<object?> jl:
                    string jsep = mc.Args.Count > 0 ? ToString(EvalExpr(mc.Args[0])) : "";
                    return string.Join(jsep, jl.Select(ToString));
                case "append" when target is List<object?> al:
                    var copy = new List<object?>(al);
                    if (mc.Args.Count > 0) copy.Add(EvalExpr(mc.Args[0]));
                    return copy;
                default:
                    return null;
            }
        }

        private object? NamespaceCall(List<ExprNode> args)
        {
            var ns = new Dictionary<string, object?>();
            foreach (var arg in args)
            {
                if (arg is KeywordArgNode kw)
                    ns[kw.Name] = EvalExpr(kw.Value);
            }
            return ns;
        }

        private object? CompareOp(ExprNode left, string op, ExprNode right)
        {
            var l = EvalExpr(left);
            var r = EvalExpr(right);
            return op switch
            {
                "==" => ValuesEqual(l, r),
                "!=" => !ValuesEqual(l, r),
                "<" => CompareValues(l, r) < 0,
                ">" => CompareValues(l, r) > 0,
                "<=" => CompareValues(l, r) <= 0,
                ">=" => CompareValues(l, r) >= 0,
                _ => throw new JinjaRenderException($"Unsupported comparison: {op}"),
            };
        }

        private static object? ArithOp(string op, object? l, object? r)
        {
            if (l is long ll && r is long lr)
            {
                switch (op)
                {
                    case "+": return ll + lr;
                    case "-": return ll - lr;
                    case "*": return ll * lr;
                    case "/": return lr == 0 ? 0L : ll / lr;
                    case "%": return lr == 0 ? 0L : ll % lr;
                }
            }
            if (l is long al && r is double rd) (l, r) = (al, rd);
            if (l is double dl && r is long rl) (l, r) = (dl, rl);
            if (l is double d1 && r is double d2)
            {
                switch (op)
                {
                    case "+": return d1 + d2;
                    case "-": return d1 - d2;
                    case "*": return d1 * d2;
                    case "/": return d2 == 0 ? 0.0 : d1 / d2;
                    case "%": return d2 == 0 ? 0.0 : d1 % d2;
                }
            }
            // String concatenation with +.
            if (op == "+" && l is string s1 && r is string s2)
                return s1 + s2;
            throw new JinjaRenderException($"Unsupported arithmetic {op} on {l?.GetType().Name} and {r?.GetType().Name}.");
        }

        private static List<object?> RangeCall(List<ExprNode> args)
        {
            long start = 0, stop, step = 1;
            switch (args.Count)
            {
                case 1:
                    stop = EvalLong(args[0]);
                    break;
                case 2:
                    start = EvalLong(args[0]);
                    stop = EvalLong(args[1]);
                    break;
                case 3:
                    start = EvalLong(args[0]);
                    stop = EvalLong(args[1]);
                    step = EvalLong(args[2]);
                    if (step == 0) throw new JinjaRenderException("range() step must not be zero.");
                    break;
                default:
                    throw new JinjaRenderException($"range() takes at most 3 arguments, got {args.Count}.");
            }
            var result = new List<object?>();
            if (step > 0)
                for (long i = start; i < stop; i += step) result.Add(i);
            else
                for (long i = start; i > stop; i += step) result.Add(i);
            return result;

            static long EvalLong(ExprNode e) => e is LiteralNode l && l.Value is long lv ? lv
                : throw new JinjaRenderException("range() arguments must be integers.");
        }

        private static bool ValuesEqual(object? a, object? b)
        {
            if (a is null && b is null) return true;
            if (a is null || b is null) return false;
            if (a is long la && b is long lb) return la == lb;
            if (a is double da && b is double db) return da == db;
            if (a is long al && b is double bd) return al == bd;
            if (a is double ad && b is long bl) return ad == bl;
            return a.Equals(b);
        }

        private static int CompareValues(object? a, object? b)
        {
            if (a is null) return b is null ? 0 : -1;
            if (b is null) return 1;
            // Normalize numerics to double so mixed long/double compares work
            // (jinja2/Python compares int and float natively).
            if (a is long al && b is long bl) return al.CompareTo(bl);
            if (a is long al2 && b is double bd) return al2.CompareTo(bd);
            if (a is double ad && b is long bl2) return ad.CompareTo(bl2);
            if (a is double da && b is double db) return da.CompareTo(db);
            if (a is string sa && b is string sb) return string.CompareOrdinal(sa, sb);
            throw new JinjaRenderException("Cannot compare incompatible types.");
        }

        private object? ApplyFilter(FilterNode node, object? input)
        {
            return node.Name switch
            {
                "trim" => input is string s ? s.Trim() : input,
                "upper" => input is string su ? su.ToUpperInvariant() : input,
                "lower" => input is string sl ? sl.ToLowerInvariant() : input,
                "length" => input switch
                {
                    string ls => (long)ls.Length,
                    List<object?> ll => (long)ll.Count,
                    _ => 0L,
                },
                "string" => ToString(input),
                "bool" => ToBool(input),
                "int" => input is double d ? (long)d : input is long l2 ? l2 : 0L,
                "float" => input is long li2 ? (double)li2 : input is double df ? df : 0.0,
                "default" => input is null && node.Args.Count > 0 ? EvalExpr(node.Args[0]) : input,
                "tojson" => ToJson(input),
                "join" => JoinFilter(input, node.Args),
                "list" => input is List<object?> l ? l : new List<object?> { input },
                "map" => MapFilter(input, node.Args),
                "replace" => input is string sr && node.Args.Count >= 2
                    ? sr.Replace(ToString(EvalExpr(node.Args[0])), ToString(EvalExpr(node.Args[1])))
                    : input,
                _ => throw new JinjaRenderException($"Unsupported filter: {node.Name}"),
            };
        }

        private object? MapFilter(object? input, List<ExprNode> args)
        {
            if (input is not List<object?> list) return input;
            if (args.Count == 0 || args[0] is not LiteralNode nameLit) return input;
            string filterName = ToString(nameLit.Value);
            return list.Select(item => ApplyFilter(
                new FilterNode(new VarNode("_"), filterName, new List<ExprNode>()), item)).ToList();
        }

        private static object? JoinFilter(object? input, List<ExprNode> args)
        {
            if (input is not List<object?> list) return input;
            string sep = args.Count > 0 && args[0] is LiteralNode ll ? ToString(ll.Value) : " ";
            return string.Join(sep, list.Select(ToString));
        }

        private static bool ToBool(object? value) => value switch
        {
            null => false,
            bool b => b,
            long l => l != 0,
            double d => d != 0,
            string s => s.Length > 0,
            List<object?> list => list.Count > 0,
            _ => true,
        };

        private static string ToString(object? value) => value switch
        {
            null => "",
            bool b => b ? "true" : "false",
            long l => l.ToString(),
            double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            string s => s,
            List<object?> list => string.Join("", list.Select(ToString)),
            _ => value.ToString() ?? "",
        };

        private static string ToJson(object? value) => value switch
        {
            null => "null",
            bool b => b ? "true" : "false",
            long l => l.ToString(),
            double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            string s => "\"" + EscapeJson(s) + "\"",
            List<object?> list => "[" + string.Join(",", list.Select(ToJson)) + "]",
            Dictionary<string, object?> dict => "{" + string.Join(
                ",", dict.Select(kv => $@"""{EscapeJson(kv.Key)}"":{ToJson(kv.Value)}")) + "}",
            _ => "\"" + EscapeJson(value.ToString() ?? "") + "\"",
        };

        private static string EscapeJson(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\r': sb.Append("\\r"); break;
                    default:
                        if (c < 0x20) sb.Append($"\\u{(int)c:x4}");
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
