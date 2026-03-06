using System.Text.RegularExpressions;

namespace DictationTool;

/// <summary>
/// Text normalization pipeline for natural speech output.
/// Transforms markdown/technical text into speech-friendly prose.
/// Ported from claude-speak's Python normalizer.
/// </summary>
public static partial class TextNormalizer
{
    private static readonly HashSet<string> CommandLangs = ["bash", "sh", "zsh", "shell"];

    // -----------------------------------------------------------------------
    // Unit expansions
    // -----------------------------------------------------------------------

    private static readonly Dictionary<string, string> UnitMap = new()
    {
        ["B"] = "bytes", ["KB"] = "kilobytes", ["MB"] = "megabytes", ["GB"] = "gigabytes",
        ["TB"] = "terabytes", ["PB"] = "petabytes",
        ["s"] = "seconds", ["ms"] = "milliseconds",
        ["ns"] = "nanoseconds", ["ps"] = "picoseconds",
        ["Hz"] = "hertz", ["kHz"] = "kilohertz", ["MHz"] = "megahertz", ["GHz"] = "gigahertz",
        ["px"] = "pixels", ["fps"] = "frames per second",
        ["bps"] = "bits per second", ["Kbps"] = "kilobits per second",
        ["Mbps"] = "megabits per second", ["Gbps"] = "gigabits per second",
    };

    // -----------------------------------------------------------------------
    // Abbreviation expansions
    // -----------------------------------------------------------------------

    private static readonly Dictionary<string, string> AbbrevMap = new()
    {
        ["API"] = "A P I", ["APIs"] = "A P I's", ["CLI"] = "C L I",
        ["TTS"] = "T T S", ["STT"] = "S T T",
        ["URL"] = "U R L", ["URLs"] = "U R L's",
        ["HTML"] = "H T M L", ["CSS"] = "C S S",
        ["JSON"] = "JSON", ["JSONL"] = "JSON L",
        ["YAML"] = "YAML", ["SQL"] = "S Q L",
        ["SSH"] = "S S H", ["HTTP"] = "H T T P", ["HTTPS"] = "H T T P S",
        ["REST"] = "REST", ["SDK"] = "S D K", ["IDE"] = "I D E",
        ["PR"] = "P R", ["PRs"] = "P R's",
        ["MCP"] = "M C P", ["LLM"] = "L L M", ["LLMs"] = "L L M's",
        ["AI"] = "A I", ["GPU"] = "G P U", ["CPU"] = "C P U", ["RAM"] = "RAM",
        ["ONNX"] = "onyx", ["NPM"] = "N P M", ["PyPI"] = "pie pie",
        ["AWS"] = "A W S", ["GCP"] = "G C P",
        ["JWT"] = "J W T", ["OAuth"] = "O Auth", ["UUID"] = "U U I D",
        ["STDIN"] = "standard in", ["STDOUT"] = "standard out", ["STDERR"] = "standard error",
        ["EOF"] = "end of file", ["PID"] = "process I D",
        ["README"] = "read me", ["TODO"] = "to do",
        ["TCP"] = "T C P", ["UDP"] = "U D P", ["DNS"] = "D N S",
        ["IP"] = "I P", ["OS"] = "O S", ["UI"] = "U I", ["UX"] = "U X",
        ["DB"] = "database", ["ORM"] = "O R M",
        ["WAV"] = "wave", ["MP3"] = "M P 3", ["PDF"] = "P D F",
        ["CSV"] = "C S V", ["XML"] = "X M L", ["SVG"] = "S V G",
        ["PNG"] = "P N G", ["JPG"] = "J P G", ["GIF"] = "gif",
        ["ASYNC"] = "async", ["CORS"] = "cors", ["REGEX"] = "regex",
        ["REPL"] = "repl", ["WSL"] = "W S L",
        ["SaaS"] = "sass",
    };

    // -----------------------------------------------------------------------
    // Language names for code blocks
    // -----------------------------------------------------------------------

    private static readonly Dictionary<string, string> LangNames = new()
    {
        ["python"] = "Python", ["py"] = "Python",
        ["bash"] = "bash", ["sh"] = "shell", ["zsh"] = "shell", ["shell"] = "shell",
        ["javascript"] = "JavaScript", ["js"] = "JavaScript",
        ["typescript"] = "TypeScript", ["ts"] = "TypeScript",
        ["json"] = "JSON", ["jsonl"] = "JSON",
        ["yaml"] = "YAML", ["yml"] = "YAML",
        ["html"] = "HTML", ["css"] = "CSS", ["sql"] = "SQL",
        ["rust"] = "Rust", ["rs"] = "Rust",
        ["go"] = "Go", ["golang"] = "Go",
        ["java"] = "Java", ["ruby"] = "Ruby", ["rb"] = "Ruby",
        ["swift"] = "Swift",
        ["c"] = "C", ["cpp"] = "C++", ["c++"] = "C++",
        ["csharp"] = "C sharp", ["cs"] = "C sharp",
        ["toml"] = "TOML", ["xml"] = "XML", ["csv"] = "CSV",
        ["dockerfile"] = "Dockerfile", ["docker"] = "Docker",
        ["makefile"] = "Makefile", ["make"] = "Makefile",
        ["markdown"] = "Markdown", ["md"] = "Markdown",
        ["plaintext"] = "text", ["text"] = "text", ["txt"] = "text",
        ["diff"] = "diff",
    };

    // -----------------------------------------------------------------------
    // File extension pronunciations
    // -----------------------------------------------------------------------

    private static readonly Dictionary<string, string> ExtMap = new()
    {
        [".py"] = "dot py", [".sh"] = "dot sh", [".js"] = "dot js", [".ts"] = "dot ts",
        [".json"] = "dot JSON", [".jsonl"] = "dot JSON L",
        [".yaml"] = "dot YAML", [".yml"] = "dot YAML",
        [".md"] = "dot md", [".txt"] = "dot text",
        [".wav"] = "dot wave", [".mp3"] = "dot M P 3",
        [".css"] = "dot C S S", [".html"] = "dot H T M L",
        [".xml"] = "dot X M L", [".csv"] = "dot C S V",
        [".env"] = "dot env", [".toml"] = "dot toml",
        [".onnx"] = "dot onyx", [".bin"] = "dot bin",
        [".rs"] = "dot rs", [".go"] = "dot go", [".java"] = "dot java",
        [".rb"] = "dot ruby", [".swift"] = "dot swift",
        [".cs"] = "dot C sharp", [".csproj"] = "dot C S proj",
        [".sln"] = "dot solution",
    };

    // -----------------------------------------------------------------------
    // Currency
    // -----------------------------------------------------------------------

    private static readonly Dictionary<char, string> CurrencyMap = new()
    {
        ['$'] = "dollar", ['€'] = "euro", ['£'] = "pound", ['¥'] = "yen",
    };

    private static readonly Dictionary<char, string> MagnitudeMap = new()
    {
        ['K'] = "thousand", ['M'] = "million", ['B'] = "billion", ['T'] = "trillion",
    };

    // -----------------------------------------------------------------------
    // Ordinal data
    // -----------------------------------------------------------------------

    private static readonly Dictionary<int, string> OnesOrdinals = new()
    {
        [1] = "first", [2] = "second", [3] = "third", [4] = "fourth", [5] = "fifth",
        [6] = "sixth", [7] = "seventh", [8] = "eighth", [9] = "ninth",
    };

    private static readonly Dictionary<int, string> TeensOrdinals = new()
    {
        [10] = "tenth", [11] = "eleventh", [12] = "twelfth", [13] = "thirteenth",
        [14] = "fourteenth", [15] = "fifteenth", [16] = "sixteenth", [17] = "seventeenth",
        [18] = "eighteenth", [19] = "nineteenth",
    };

    private static readonly Dictionary<int, string> TensOrdinals = new()
    {
        [20] = "twentieth", [30] = "thirtieth", [40] = "fortieth", [50] = "fiftieth",
        [60] = "sixtieth", [70] = "seventieth", [80] = "eightieth", [90] = "ninetieth",
    };

    private static readonly Dictionary<int, string> TensCardinal = new()
    {
        [20] = "twenty", [30] = "thirty", [40] = "forty", [50] = "fifty",
        [60] = "sixty", [70] = "seventy", [80] = "eighty", [90] = "ninety",
    };

    private static readonly Dictionary<int, string> CardinalWords = new()
    {
        [1] = "one", [2] = "two", [3] = "three", [4] = "four", [5] = "five",
        [6] = "six", [7] = "seven", [8] = "eight", [9] = "nine", [10] = "ten",
    };

    private static readonly Dictionary<int, string> FractionDenom = new()
    {
        [2] = "half", [3] = "third", [4] = "quarter", [5] = "fifth",
        [6] = "sixth", [7] = "seventh", [8] = "eighth", [9] = "ninth", [10] = "tenth",
    };

    private static readonly Dictionary<int, string> MonthNames = new()
    {
        [1] = "January", [2] = "February", [3] = "March", [4] = "April",
        [5] = "May", [6] = "June", [7] = "July", [8] = "August",
        [9] = "September", [10] = "October", [11] = "November", [12] = "December",
    };

    private static readonly string[] Ordinals =
        ["First", "Second", "Third", "Fourth", "Fifth", "Sixth", "Seventh", "Eighth", "Ninth", "Tenth"];

    private static readonly Dictionary<string, string> DigitWords = new()
    {
        ["0"] = "zero", ["1"] = "one", ["2"] = "two", ["3"] = "three", ["4"] = "four",
        ["5"] = "five", ["6"] = "six", ["7"] = "seven", ["8"] = "eight", ["9"] = "nine",
    };

    private static readonly Dictionary<string, string> SlashCommon = new()
    {
        ["and/or"] = "and or",
        ["true/false"] = "true or false",
        ["yes/no"] = "yes or no",
        ["input/output"] = "input output",
    };

    // -----------------------------------------------------------------------
    // Pre-compiled regex patterns
    // -----------------------------------------------------------------------

    [GeneratedRegex(@"```(\w*)\s*\n(.*?)```", RegexOptions.Singleline)]
    private static partial Regex ReFencedCode();

    [GeneratedRegex(@"^(\d+)[.)]\s+(.*)")]
    private static partial Regex ReNumberedItem();

    [GeneratedRegex(@"^( *)[-*+]\s+(.*)")]
    private static partial Regex ReBulletItem();

    [GeneratedRegex(@"https?://github\.com/\S+")]
    private static partial Regex ReGitHubUrl();

    [GeneratedRegex(@"https?://docs\.python\.org\S*")]
    private static partial Regex RePythonDocsUrl();

    [GeneratedRegex(@"https?://localhost(?::(\d+))?\S*")]
    private static partial Regex ReLocalhostUrl();

    [GeneratedRegex(@"https?://([a-zA-Z0-9.-]+)(?:/\S*)?")]
    private static partial Regex ReGenericUrl();

    [GeneratedRegex(@"\b([a-zA-Z0-9_.+-]+)@([a-zA-Z0-9-]+\.[a-zA-Z0-9-.]+)\b")]
    private static partial Regex ReEmail();

    [GeneratedRegex(@"(?<!\S)([a-zA-Z0-9-]+\.(?:com|org|net|io|dev|co|edu|gov|app|ai|py))\b")]
    private static partial Regex ReBareDomain();

    [GeneratedRegex(@"([$€£¥])(\d+(?:\.\d+)?)\s*([KMBTkmbt])\b")]
    private static partial Regex ReCurrencyMag();

    [GeneratedRegex(@"([$€£¥])(\d+)\.(\d{2})\b")]
    private static partial Regex ReCurrencyCents();

    [GeneratedRegex(@"([$€£¥])(\d+(?:\.\d+)?)\b")]
    private static partial Regex ReCurrencyPlain();

    [GeneratedRegex(@"(\d+(?:\.\d+)?)%")]
    private static partial Regex RePercent();

    [GeneratedRegex(@"\b(\d{1,2})(st|nd|rd|th)\b")]
    private static partial Regex ReOrdinal();

    [GeneratedRegex(@"\b(\d{1,3})((?:,\d{3})+)\b")]
    private static partial Regex ReNumberCommas();

    [GeneratedRegex(@"(?<![/\w])(\d+)/(\d+)(?![/\w])")]
    private static partial Regex ReFraction();

    [GeneratedRegex(@"\b(\d+):(\d+)\b(?!\s*(?:AM|PM|am|pm))")]
    private static partial Regex ReRatio();

    [GeneratedRegex(@"\b(\d{1,2}):(\d{2})\s*(am|pm|AM|PM)\b")]
    private static partial Regex ReTimeAmPm();

    [GeneratedRegex(@"\b([01]?\d|2[0-3]):([0-5]\d)\b")]
    private static partial Regex ReTime24H();

    [GeneratedRegex(@"\b(\d{4})-(0[1-9]|1[0-2])-(0[1-9]|[12]\d|3[01])\b")]
    private static partial Regex ReIsoDate();

    [GeneratedRegex(@"(-?\d+(?:\.\d+)?)\s*°\s*([FCfc])\b")]
    private static partial Regex ReTemp();

    [GeneratedRegex(@"(\S+)\s*!=\s*(\S+)")]
    private static partial Regex ReNotEquals();

    [GeneratedRegex(@"(\S+)\s*==\s*(\S+)")]
    private static partial Regex ReDoubleEquals();

    [GeneratedRegex(@"(\S+)\s*>=\s*(\S+)")]
    private static partial Regex ReGte();

    [GeneratedRegex(@"(\S+)\s*<=\s*(\S+)")]
    private static partial Regex ReLte();

    [GeneratedRegex(@"(\S+) > (\S+)")]
    private static partial Regex ReGt();

    [GeneratedRegex(@"(\S+) < (\S+)")]
    private static partial Regex ReLt();

    [GeneratedRegex(@"(\S+) \+ (\S+)")]
    private static partial Regex RePlus();

    [GeneratedRegex(@"(\S+) - (\S+)")]
    private static partial Regex ReMinus();

    [GeneratedRegex(@"(\S+) \* (\S+)")]
    private static partial Regex ReTimes();

    [GeneratedRegex(@"(\S+) = (\S+)")]
    private static partial Regex ReEquals();

    [GeneratedRegex(@"(\w+)\^2\b")]
    private static partial Regex RePower2();

    [GeneratedRegex(@"(\w+)\^3\b")]
    private static partial Regex RePower3();

    [GeneratedRegex(@"(\w+)\^(\w+)")]
    private static partial Regex RePowerN();

    [GeneratedRegex(@"\b([a-zA-Z]+)/([a-zA-Z]+)\b")]
    private static partial Regex ReSlashPair();

    [GeneratedRegex(@"\bw/o\b")]
    private static partial Regex ReWithout();

    [GeneratedRegex(@"\bw/(?=\s)")]
    private static partial Regex ReWith();

    [GeneratedRegex(@"\be\.g\.\s?")]
    private static partial Regex ReEg();

    [GeneratedRegex(@"\bi\.e\.\s?")]
    private static partial Regex ReIe();

    [GeneratedRegex(@"\betc\.")]
    private static partial Regex ReEtc();

    [GeneratedRegex(@"\bvs\.\s?")]
    private static partial Regex ReVs();

    [GeneratedRegex(@"/(?:[\w.~-]+/){2,}[\w.~-]+")]
    private static partial Regex ReLongPath();

    [GeneratedRegex(@"~(?:/[\w.~-]+){2,}")]
    private static partial Regex ReHomePath();

    [GeneratedRegex(@"\bsrc/")]
    private static partial Regex ReSrcPrefix();

    [GeneratedRegex(@"\b(\d+)\.(\d+)(x)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReDecimal();

    [GeneratedRegex(@"(?:(version)\s+)?[vV](\d+(?:\.\d+)+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReVersionV();

    [GeneratedRegex(@"\b(version)\s+(\d+(?:\.\d+)+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReVersionBare();

    [GeneratedRegex(@"\s*[\u2014\u2013]\s*")]
    private static partial Regex ReEmDash();

    [GeneratedRegex(@"[{}]")]
    private static partial Regex ReCurly();

    [GeneratedRegex(@"[\[\]]")]
    private static partial Regex ReSquare();

    [GeneratedRegex(@"\(\s*\)")]
    private static partial Regex ReEmptyParens();

    [GeneratedRegex(@"\(([^)]{1,200})\)")]
    private static partial Regex ReParenthetical();

    [GeneratedRegex(@"  +")]
    private static partial Regex ReMultiSpace();

    [GeneratedRegex(@"~(\d)")]
    private static partial Regex ReTildeNum();

    [GeneratedRegex(@"~/")]
    private static partial Regex ReTildePath();

    [GeneratedRegex(@"^#+\s?", RegexOptions.Multiline)]
    private static partial Regex ReHashHeading();

    [GeneratedRegex(@"\b([a-z]+)_([a-z]+(?:_[a-z]+)*)\b")]
    private static partial Regex ReSnakeCase();

    [GeneratedRegex(@"\b(\w+)-(\w+)-(\w+)\b")]
    private static partial Regex ReTripleHyphen();

    [GeneratedRegex(@"\b(\w+)-(\w+)\b")]
    private static partial Regex ReDoubleHyphen();

    [GeneratedRegex(@"#(\w+)")]
    private static partial Regex ReHashtagWord();

    [GeneratedRegex(@"@(\w+)")]
    private static partial Regex ReAtWord();

    [GeneratedRegex(@",\s*,")]
    private static partial Regex ReDoubleComma();

    [GeneratedRegex(@"^\s*,\s*", RegexOptions.Multiline)]
    private static partial Regex ReLeadingComma();

    [GeneratedRegex(@"\s*,\s*$", RegexOptions.Multiline)]
    private static partial Regex ReTrailingComma();

    [GeneratedRegex(@",(\s*,)+")]
    private static partial Regex ReMultiComma();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ReMultiNewline();

    [GeneratedRegex(@"^[\s,.:;!?\-]+$")]
    private static partial Regex RePunctOnlyLine();

    [GeneratedRegex(@"CI/CD")]
    private static partial Regex ReCiCd();

    // -----------------------------------------------------------------------
    // Helper: number to ordinal word
    // -----------------------------------------------------------------------

    private static string NumberToOrdinal(int n)
    {
        if (n <= 0 || n > 99) return $"{n}th";
        if (n < 10) return OnesOrdinals.GetValueOrDefault(n, $"{n}th");
        if (n < 20) return TeensOrdinals.GetValueOrDefault(n, $"{n}th");
        var (tens, ones) = (n / 10 * 10, n % 10);
        if (ones == 0) return TensOrdinals.GetValueOrDefault(n, $"{n}th");
        var tensWord = TensCardinal.GetValueOrDefault(tens, "");
        var onesOrdinal = OnesOrdinals.GetValueOrDefault(ones, $"{ones}th");
        return $"{tensWord} {onesOrdinal}";
    }

    // -----------------------------------------------------------------------
    // Transform: describe_code_blocks
    // -----------------------------------------------------------------------

    public static string DescribeCodeBlocks(string text)
    {
        return ReFencedCode().Replace(text, m =>
        {
            var langTag = (m.Groups[1].Value ?? "").Trim().ToLowerInvariant();
            var body = m.Groups[2].Value.Trim();
            var isCommand = CommandLangs.Contains(langTag);

            if (string.IsNullOrEmpty(langTag) && !string.IsNullOrEmpty(body))
            {
                var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length <= 2) isCommand = true;
            }

            if (LangNames.TryGetValue(langTag, out var langName))
            {
                var noun = isCommand ? "command" : "code snippet";
                return $"Here is a {langName} {noun}.";
            }
            else
            {
                var noun = isCommand ? "command" : "code block";
                return $"Here is a {noun}.";
            }
        });
    }

    // -----------------------------------------------------------------------
    // Transform: narrate_tables
    // -----------------------------------------------------------------------

    public static string NarrateTables(string text)
    {
        static string OxfordJoin(List<string> items) => items.Count switch
        {
            0 => "",
            1 => items[0],
            2 => $"{items[0]} and {items[1]}",
            _ => string.Join(", ", items.Take(items.Count - 1)) + ", and " + items[^1],
        };

        static List<string> ParseRow(string line)
        {
            var cells = line.Trim().Trim('|').Split('|');
            return cells.Select(c => c.Trim()).Where(c => !string.IsNullOrEmpty(c)).ToList();
        }

        static bool IsSeparator(string line)
        {
            var stripped = line.Trim().Trim('|').Replace("|", "");
            return !string.IsNullOrEmpty(stripped) && stripped.All(c => c is '-' or ':' or ' ');
        }

        var lines = text.Split('\n');
        var result = new List<string>();
        int i = 0;

        while (i < lines.Length)
        {
            if (i + 1 < lines.Length
                && lines[i].Trim().StartsWith('|')
                && IsSeparator(lines[i + 1]))
            {
                var headers = ParseRow(lines[i]);
                i += 2;

                var rows = new List<List<string>>();
                while (i < lines.Length && lines[i].Trim().StartsWith('|'))
                {
                    if (!IsSeparator(lines[i]))
                        rows.Add(ParseRow(lines[i]));
                    i++;
                }

                var headerText = OxfordJoin(headers);
                if (rows.Count > 5)
                {
                    result.Add($"A table with {rows.Count} rows showing {headerText}.");
                }
                else
                {
                    var parts = new List<string> { $"A table with columns {headerText}." };
                    for (int idx = 0; idx < rows.Count; idx++)
                        parts.Add($"Row {idx + 1}: {string.Join(", ", rows[idx])}.");
                    result.Add(string.Join(" ", parts));
                }
            }
            else
            {
                result.Add(lines[i]);
                i++;
            }
        }

        return string.Join("\n", result);
    }

    // -----------------------------------------------------------------------
    // Transform: improve_lists
    // -----------------------------------------------------------------------

    public static string ImproveLists(string text)
    {
        var lines = text.Split('\n');
        var result = new List<string>();
        int i = 0;

        while (i < lines.Length)
        {
            var stripped = lines[i].Trim();

            // Numbered list
            var numMatch = ReNumberedItem().Match(stripped);
            if (numMatch.Success)
            {
                var items = new List<string>();
                var m = numMatch;
                while (m.Success)
                {
                    items.Add(m.Groups[2].Value);
                    i++;
                    m = i < lines.Length ? ReNumberedItem().Match(lines[i].Trim()) : Match.Empty;
                }
                for (int idx = 0; idx < items.Count; idx++)
                {
                    var ordinal = idx < Ordinals.Length ? Ordinals[idx] : $"Number {idx + 1}";
                    var itemText = items[idx].TrimEnd().TrimEnd('.');
                    result.Add($"{ordinal}, {itemText}.");
                }
                continue;
            }

            // Bullet list
            var bulletMatch = ReBulletItem().Match(lines[i]);
            if (bulletMatch.Success)
            {
                var bulletItems = new List<(int indent, string content)>();
                var bm = bulletMatch;
                while (bm.Success)
                {
                    bulletItems.Add((bm.Groups[1].Value.Length, bm.Groups[2].Value));
                    i++;
                    bm = i < lines.Length ? ReBulletItem().Match(lines[i]) : Match.Empty;
                }
                var baseIndent = bulletItems.Min(b => b.indent);
                foreach (var (indent, content) in bulletItems)
                {
                    var contentText = content.TrimEnd().TrimEnd('.');
                    result.Add(indent > baseIndent ? $"Sub-item, {contentText}." : $"{contentText}.");
                }
                continue;
            }

            result.Add(lines[i]);
            i++;
        }

        return string.Join("\n", result);
    }

    // -----------------------------------------------------------------------
    // Transform: strip_code_blocks
    // -----------------------------------------------------------------------

    public static string StripCodeBlocks(string text)
    {
        var lines = text.Split('\n');
        var cleaned = new List<string>();
        foreach (var line in lines)
        {
            var stripped = line.Trim();
            if (string.IsNullOrEmpty(stripped)) { cleaned.Add(line); continue; }
            if (stripped.StartsWith("$ ") || stripped.StartsWith(">>> ")) continue;
            int alpha = stripped.Count(c => char.IsLetter(c) || c == ' ');
            if (stripped.Length > 15 && (double)alpha / stripped.Length < 0.3) continue;
            cleaned.Add(line);
        }
        return string.Join("\n", cleaned);
    }

    // -----------------------------------------------------------------------
    // Transform: clean_urls_and_emails
    // -----------------------------------------------------------------------

    public static string CleanUrlsAndEmails(string text)
    {
        text = ReGitHubUrl().Replace(text, "a github link");
        text = RePythonDocsUrl().Replace(text, "a python docs link");
        text = ReLocalhostUrl().Replace(text, m =>
        {
            var port = m.Groups[1].Value;
            return string.IsNullOrEmpty(port) ? "localhost" : $"localhost {port}";
        });
        text = ReGenericUrl().Replace(text, m =>
        {
            var domain = m.Groups[1].Value.Replace(".", " dot ");
            return $"a link to {domain}";
        });
        text = ReEmail().Replace(text, m =>
        {
            var user = m.Groups[1].Value;
            var domain = m.Groups[2].Value.Replace(".", " dot ");
            return $"{user} at {domain}";
        });
        text = ReBareDomain().Replace(text, m => m.Groups[1].Value.Replace(".", " dot "));
        return text;
    }

    // -----------------------------------------------------------------------
    // Transform: expand_currency
    // -----------------------------------------------------------------------

    public static string ExpandCurrency(string text)
    {
        static string Pluralize(string baseName) => baseName switch
        {
            "dollar" => "dollars", "euro" => "euros", "pound" => "pounds", _ => baseName,
        };

        text = ReCurrencyMag().Replace(text, m =>
        {
            var sym = m.Groups[1].Value[0];
            var amount = m.Groups[2].Value;
            var mag = char.ToUpper(m.Groups[3].Value[0]);
            var baseName = CurrencyMap.GetValueOrDefault(sym, "dollar");
            var magWord = MagnitudeMap.GetValueOrDefault(mag, "");
            return $"{amount} {magWord} {Pluralize(baseName)}";
        });

        text = ReCurrencyCents().Replace(text, m =>
        {
            var sym = m.Groups[1].Value[0];
            var whole = m.Groups[2].Value;
            var cents = m.Groups[3].Value;
            var baseName = CurrencyMap.GetValueOrDefault(sym, "dollar");
            return baseName switch
            {
                "dollar" => $"{whole} {(whole == "1" ? "dollar" : "dollars")} and {cents} {(cents == "01" ? "cent" : "cents")}",
                "euro" => $"{whole} {(whole == "1" ? "euro" : "euros")} and {cents} {(cents == "01" ? "cent" : "cents")}",
                "pound" => $"{whole} {(whole == "1" ? "pound" : "pounds")} and {cents} pence",
                _ => $"{whole} {baseName}",
            };
        });

        text = ReCurrencyPlain().Replace(text, m =>
        {
            var sym = m.Groups[1].Value[0];
            var amount = m.Groups[2].Value;
            var baseName = CurrencyMap.GetValueOrDefault(sym, "dollar");
            var isSingular = amount is "1" or "1.0";
            var word = isSingular ? baseName : Pluralize(baseName);
            return $"{amount} {word}";
        });

        return text;
    }

    // -----------------------------------------------------------------------
    // Transform: expand_percentages
    // -----------------------------------------------------------------------

    public static string ExpandPercentages(string text)
        => RePercent().Replace(text, "$1 percent");

    // -----------------------------------------------------------------------
    // Transform: expand_ordinals
    // -----------------------------------------------------------------------

    public static string ExpandOrdinals(string text)
        => ReOrdinal().Replace(text, m =>
        {
            var num = int.Parse(m.Groups[1].Value);
            return (num < 1 || num > 99) ? m.Value : NumberToOrdinal(num);
        });

    // -----------------------------------------------------------------------
    // Transform: strip_number_commas
    // -----------------------------------------------------------------------

    public static string StripNumberCommas(string text)
        => ReNumberCommas().Replace(text, m => m.Groups[1].Value + m.Groups[2].Value.Replace(",", ""));

    // -----------------------------------------------------------------------
    // Transform: expand_fractions_ratios
    // -----------------------------------------------------------------------

    public static string ExpandFractionsRatios(string text)
    {
        text = ReFraction().Replace(text, m =>
        {
            var num = int.Parse(m.Groups[1].Value);
            var den = int.Parse(m.Groups[2].Value);
            if (!CardinalWords.TryGetValue(num, out var numWord) ||
                !FractionDenom.TryGetValue(den, out var denWord))
                return m.Value;

            if (den == 2) return num == 1 ? $"{numWord} {denWord}" : $"{numWord} halves";
            if (num == 1) return $"{numWord} {denWord}";
            return denWord == "quarter" ? $"{numWord} quarters" : $"{numWord} {denWord}s";
        });

        text = ReRatio().Replace(text, "$1 to $2");
        return text;
    }

    // -----------------------------------------------------------------------
    // Transform: expand_time_formats
    // -----------------------------------------------------------------------

    public static string ExpandTimeFormats(string text)
    {
        text = ReTimeAmPm().Replace(text, m =>
        {
            var h = m.Groups[1].Value;
            var mins = m.Groups[2].Value;
            var ampm = m.Groups[3].Value.ToUpperInvariant();
            return $"{h}:{mins} {ampm}";
        });

        var result = new List<string>();
        int lastEnd = 0;
        foreach (Match m in ReTime24H().Matches(text))
        {
            int hour = int.Parse(m.Groups[1].Value);
            int minute = int.Parse(m.Groups[2].Value);
            if (hour > 23 || minute > 59) continue;

            var end = m.Index + m.Length;
            var rest = end < text.Length ? text[end..Math.Min(end + 5, text.Length)].Trim().ToUpperInvariant() : "";
            if (rest.StartsWith("AM") || rest.StartsWith("PM")) continue;

            result.Add(text[lastEnd..m.Index]);

            int spokenHour;
            string period;
            if (hour == 0) { spokenHour = 12; period = "AM"; }
            else if (hour < 12) { spokenHour = hour; period = "AM"; }
            else if (hour == 12) { spokenHour = 12; period = "PM"; }
            else { spokenHour = hour - 12; period = "PM"; }

            result.Add(minute == 0 ? $"{spokenHour} {period}" : $"{spokenHour}:{m.Groups[2].Value} {period}");
            lastEnd = end;
        }
        result.Add(text[lastEnd..]);
        return string.Concat(result);
    }

    // -----------------------------------------------------------------------
    // Transform: expand_dates
    // -----------------------------------------------------------------------

    public static string ExpandDates(string text)
        => ReIsoDate().Replace(text, m =>
        {
            var year = m.Groups[1].Value;
            var month = int.Parse(m.Groups[2].Value);
            var day = int.Parse(m.Groups[3].Value);
            var monthName = MonthNames.GetValueOrDefault(month, m.Groups[2].Value);
            return $"{monthName} {day}, {year}";
        });

    // -----------------------------------------------------------------------
    // Transform: expand_temperature
    // -----------------------------------------------------------------------

    public static string ExpandTemperature(string text)
        => ReTemp().Replace(text, m =>
        {
            var num = m.Groups[1].Value;
            var scale = char.ToUpper(m.Groups[2].Value[0]) == 'F' ? "Fahrenheit" : "Celsius";
            if (num.StartsWith('-')) num = "negative " + num[1..];
            return $"{num} degrees {scale}";
        });

    // -----------------------------------------------------------------------
    // Transform: expand_math_operators
    // -----------------------------------------------------------------------

    public static string ExpandMathOperators(string text)
    {
        text = ReNotEquals().Replace(text, "$1 not equals $2");
        text = ReDoubleEquals().Replace(text, "$1 equals $2");
        text = ReGte().Replace(text, "$1 greater than or equal to $2");
        text = ReLte().Replace(text, "$1 less than or equal to $2");
        text = ReGt().Replace(text, "$1 greater than $2");
        text = ReLt().Replace(text, "$1 less than $2");
        text = RePlus().Replace(text, "$1 plus $2");
        text = ReMinus().Replace(text, "$1 minus $2");
        text = ReTimes().Replace(text, "$1 times $2");
        text = ReEquals().Replace(text, "$1 equals $2");
        text = RePower2().Replace(text, "$1 squared");
        text = RePower3().Replace(text, "$1 cubed");
        text = RePowerN().Replace(text, "$1 to the $2");
        return text;
    }

    // -----------------------------------------------------------------------
    // Transform: expand_slash_pairs
    // -----------------------------------------------------------------------

    public static string ExpandSlashPairs(string text)
    {
        foreach (var (pair, replacement) in SlashCommon)
            text = text.Replace(pair, replacement);

        text = ReSlashPair().Replace(text, m =>
        {
            var start = m.Index;
            if (start > 0 && text[start - 1] is '/' or '.' or '\\') return m.Value;
            var end = m.Index + m.Length;
            if (end < text.Length && text[end] is '/' or '.') return m.Value;
            return $"{m.Groups[1].Value} or {m.Groups[2].Value}";
        });

        return text;
    }

    // -----------------------------------------------------------------------
    // Transform: expand_stop_words
    // -----------------------------------------------------------------------

    public static string ExpandStopWords(string text)
    {
        text = ReWithout().Replace(text, "without");
        text = ReWith().Replace(text, "with");
        text = ReEg().Replace(text, "for example ");
        text = ReIe().Replace(text, "that is ");
        text = ReEtc().Replace(text, "etcetera");
        text = ReVs().Replace(text, "versus ");
        return text;
    }

    // -----------------------------------------------------------------------
    // Transform: expand_units
    // -----------------------------------------------------------------------

    private static readonly Regex ReUnits = BuildUnitRegex();

    private static Regex BuildUnitRegex()
    {
        var sorted = UnitMap.Keys.OrderByDescending(k => k.Length);
        var pattern = @"(\d+(?:\.\d+)?)\s*(" + string.Join("|", sorted.Select(Regex.Escape)) + @")\b";
        return new Regex(pattern, RegexOptions.Compiled);
    }

    public static string ExpandUnits(string text)
        => ReUnits.Replace(text, m =>
        {
            var number = m.Groups[1].Value;
            var unitKey = m.Groups[2].Value;
            var unit = UnitMap.GetValueOrDefault(unitKey, unitKey);
            if (number == "1" && unit.EndsWith('s')) unit = unit[..^1];
            return $"{number} {unit}";
        });

    // -----------------------------------------------------------------------
    // Transform: expand_abbreviations
    // -----------------------------------------------------------------------

    private static readonly Dictionary<string, Regex> AbbrevRegexes = AbbrevMap.ToDictionary(
        kv => kv.Key,
        kv => new Regex(@"\b" + Regex.Escape(kv.Key) + @"\b", RegexOptions.Compiled)
    );

    public static string ExpandAbbreviations(string text)
    {
        // Handle CI/CD before general abbreviations (the slash would break \b matching)
        text = ReCiCd().Replace(text, "C I C D");

        foreach (var (abbr, expansion) in AbbrevMap)
            text = AbbrevRegexes[abbr].Replace(text, expansion);

        return text;
    }

    // -----------------------------------------------------------------------
    // Transform: clean_file_paths
    // -----------------------------------------------------------------------

    public static string CleanFilePaths(string text)
    {
        text = ReSrcPrefix().Replace(text, "source ");
        text = ReLongPath().Replace(text, m => m.Value.TrimEnd('/').Split('/')[^1]);
        text = ReHomePath().Replace(text, m => m.Value.Split('/')[^1]);
        return text;
    }

    // -----------------------------------------------------------------------
    // Transform: clean_file_extensions
    // -----------------------------------------------------------------------

    private static readonly Dictionary<string, Regex> ExtRegexes = ExtMap.ToDictionary(
        kv => kv.Key,
        kv => new Regex(Regex.Escape(kv.Key) + @"\b", RegexOptions.Compiled)
    );

    public static string CleanFileExtensions(string text)
    {
        foreach (var (ext, spoken) in ExtMap)
            text = ExtRegexes[ext].Replace(text, " " + spoken);
        return text;
    }

    // -----------------------------------------------------------------------
    // Transform: speak_decimal_numbers
    // -----------------------------------------------------------------------

    public static string SpeakDecimalNumbers(string text)
        => ReDecimal().Replace(text, m =>
        {
            var whole = m.Groups[1].Value;
            var frac = m.Groups[2].Value;
            var suffix = m.Groups[3].Value;
            var fracSpoken = string.Join(" ", frac.Select(d => DigitWords.GetValueOrDefault(d.ToString(), d.ToString())));
            return string.IsNullOrEmpty(suffix)
                ? $"{whole} point {fracSpoken}"
                : $"{whole} point {fracSpoken} {suffix}";
        });

    // -----------------------------------------------------------------------
    // Transform: clean_version_strings
    // -----------------------------------------------------------------------

    public static string CleanVersionStrings(string text)
    {
        static string SpeakVersion(Match m)
        {
            var spoken = string.Join(" point ", m.Groups[2].Value.Split('.'));
            return $"version {spoken}";
        }

        text = ReVersionV().Replace(text, SpeakVersion);
        text = ReVersionBare().Replace(text, SpeakVersion);
        return text;
    }

    // -----------------------------------------------------------------------
    // Transform: clean_technical_punctuation
    // -----------------------------------------------------------------------

    public static string CleanTechnicalPunctuation(string text)
    {
        text = text.Replace("→", ", ").Replace("←", ", ");
        text = text.Replace("=>", ", ").Replace("->", ", ");
        text = ReEmDash().Replace(text, ", ");
        text = text.Replace("...", ". ");
        text = text.Replace("`", "");
        text = text.Replace("&", " and ");
        text = ReHashtagWord().Replace(text, "hashtag $1");
        text = ReAtWord().Replace(text, "at $1");
        text = ReCurly().Replace(text, "");
        text = ReSquare().Replace(text, "");
        text = ReEmptyParens().Replace(text, "");
        text = ReParenthetical().Replace(text, ", $1,");
        text = ReMultiSpace().Replace(text, " ");
        text = ReTildeNum().Replace(text, "about $1");
        text = ReTildePath().Replace(text, "/");
        text = text.Replace("~", "");
        text = text.Replace("*", "");
        text = ReHashHeading().Replace(text, "");
        text = text.Replace("|", " ");
        text = ReSnakeCase().Replace(text, m => m.Value.Replace('_', ' '));
        text = ReTripleHyphen().Replace(text, "$1 $2 $3");
        text = ReDoubleHyphen().Replace(text, "$1 $2");
        return text;
    }

    // -----------------------------------------------------------------------
    // Transform: final_cleanup
    // -----------------------------------------------------------------------

    public static string FinalCleanup(string text)
    {
        text = ReDoubleComma().Replace(text, ",");
        text = ReLeadingComma().Replace(text, "");
        text = ReTrailingComma().Replace(text, "");
        text = ReMultiComma().Replace(text, ",");
        text = ReMultiSpace().Replace(text, " ");
        text = ReMultiNewline().Replace(text, "\n\n");

        var lines = text.Split('\n')
            .Where(ln => !string.IsNullOrWhiteSpace(ln) && !RePunctOnlyLine().IsMatch(ln.Trim()))
            .ToList();

        for (int i = 0; i < lines.Count; i++)
        {
            var stripped = lines[i].TrimEnd();
            if (!string.IsNullOrEmpty(stripped) && !".!?;:".Contains(stripped[^1]))
                lines[i] = stripped + ".";
        }

        return string.Join(" ", lines.Select(ln => ln.Trim()).Where(ln => !string.IsNullOrEmpty(ln)));
    }

    // -----------------------------------------------------------------------
    // Public API: full normalization pipeline
    // -----------------------------------------------------------------------

    public static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        text = DescribeCodeBlocks(text);
        text = NarrateTables(text);
        text = ImproveLists(text);
        text = StripCodeBlocks(text);
        text = CleanUrlsAndEmails(text);
        text = CleanFilePaths(text);
        text = ExpandCurrency(text);
        text = ExpandPercentages(text);
        text = ExpandOrdinals(text);
        text = StripNumberCommas(text);
        text = ExpandTimeFormats(text);
        text = ExpandFractionsRatios(text);
        text = ExpandDates(text);
        text = ExpandTemperature(text);
        text = ExpandMathOperators(text);
        text = ExpandUnits(text);
        text = CleanVersionStrings(text);
        text = SpeakDecimalNumbers(text);
        text = CleanFileExtensions(text);
        text = ExpandStopWords(text);
        text = ExpandAbbreviations(text);
        text = ExpandSlashPairs(text);
        text = CleanTechnicalPunctuation(text);
        text = FinalCleanup(text);
        return text;
    }
}
