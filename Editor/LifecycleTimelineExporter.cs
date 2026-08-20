using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AceLand.Lifecycle.Editor
{
    /// <summary>
    /// Serialises a <see cref="LifecycleTimeline"/> snapshot to JSON or CSV for offline analysis.
    /// JSON is hand-written (enums as strings, clean nulls, escaped) and validated before it is
    /// returned or written to disk. CSV opens with a phase-summary header block followed by the
    /// per-module table. All numbers use <see cref="CultureInfo.InvariantCulture"/>.
    /// </summary>
    internal static class LifecycleTimelineExporter
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ── Public entry points ─────────────────────────────────────────────

        /// <summary>
        /// Prompts for a save location and writes the timeline in the given format.
        /// Returns the written path, or null if the user cancelled or nothing was written.
        /// </summary>
        internal static string ExportWithDialog(LifecycleTimeline timeline, TimelineExportFormat format)
        {
            if (timeline == null || timeline.IsEmpty)
            {
                EditorUtility.DisplayDialog("Export Timeline",
                    "There is no captured timeline to export.", "OK");
                return null;
            }

            var isJson = format == TimelineExportFormat.Json;
            var ext = isJson ? "json" : "csv";
            var defaultName = $"lifecycle-timeline-{DateTime.Now:yyyyMMdd-HHmmss}.{ext}";

            var path = EditorUtility.SaveFilePanel(
                isJson ? "Export Timeline as JSON" : "Export Timeline as CSV",
                "", defaultName, ext);

            if (string.IsNullOrEmpty(path)) return null;

            try
            {
                var content = isJson ? ToJson(timeline) : ToCsv(timeline);
                File.WriteAllText(path, content, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Export Timeline",
                    $"Failed to export timeline:\n{e.Message}", "OK");
                return null;
            }

            return path;
        }

        // ── JSON ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Serialises the timeline to a JSON document. The result is validated (well-formed check)
        /// before being returned; an <see cref="InvalidOperationException"/> is thrown if the
        /// generated text does not parse, so a corrupt file is never written.
        /// </summary>
        internal static string ToJson(LifecycleTimeline timeline)
        {
            if (timeline == null) throw new ArgumentNullException(nameof(timeline));

            var sb = new StringBuilder(4096);
            sb.Append('{').Append('\n');

            // Run-level summary.
            Indent(sb, 1).Append("\"totalMs\": ").Append(Num(timeline.TotalMs)).Append(",\n");
            Indent(sb, 1).Append("\"problemCount\": ").Append(timeline.ProblemCount).Append(",\n");
            Indent(sb, 1).Append("\"moduleCount\": ").Append(timeline.Modules.Count).Append(",\n");

            // Phases array.
            Indent(sb, 1).Append("\"phases\": [");
            if (timeline.Phases.Count == 0)
            {
                sb.Append("],\n");
            }
            else
            {
                sb.Append('\n');
                for (var i = 0; i < timeline.Phases.Count; i++)
                {
                    var p = timeline.Phases[i];
                    Indent(sb, 2).Append('{').Append('\n');
                    Indent(sb, 3).Append("\"phase\": ").Append(Str(p.Phase.ToString())).Append(",\n");
                    Indent(sb, 3).Append("\"startedAtMs\": ").Append(Num(p.StartedAtMs)).Append(",\n");
                    Indent(sb, 3).Append("\"endedAtMs\": ").Append(Num(p.EndedAtMs)).Append(",\n");
                    Indent(sb, 3).Append("\"durationMs\": ").Append(Num(p.DurationMs)).Append(",\n");
                    Indent(sb, 3).Append("\"moduleCount\": ").Append(p.ModuleCount).Append(",\n");
                    Indent(sb, 3).Append("\"batches\": ").Append(p.Batches).Append(",\n");
                    Indent(sb, 3).Append("\"timedOut\": ").Append(Bool(p.TimedOut)).Append('\n');
                    Indent(sb, 2).Append('}').Append(i < timeline.Phases.Count - 1 ? ",\n" : "\n");
                }
                Indent(sb, 1).Append("],\n");
            }

            // Modules array.
            Indent(sb, 1).Append("\"modules\": [");
            if (timeline.Modules.Count == 0)
            {
                sb.Append("]\n");
            }
            else
            {
                sb.Append('\n');
                for (var i = 0; i < timeline.Modules.Count; i++)
                {
                    var m = timeline.Modules[i];
                    Indent(sb, 2).Append('{').Append('\n');
                    Indent(sb, 3).Append("\"displayName\": ").Append(Str(m.DisplayName)).Append(",\n");
                    Indent(sb, 3).Append("\"id\": ").Append(Str(m.Id != null ? m.Id.FullName : null)).Append(",\n");
                    Indent(sb, 3).Append("\"phase\": ").Append(Str(m.Phase.ToString())).Append(",\n");
                    Indent(sb, 3).Append("\"state\": ").Append(Str(m.State.ToString())).Append(",\n");
                    Indent(sb, 3).Append("\"level\": ").Append(m.Level).Append(",\n");
                    Indent(sb, 3).Append("\"sortIndex\": ").Append(m.SortIndex).Append(",\n");
                    Indent(sb, 3).Append("\"isAsync\": ").Append(Bool(m.IsAsync)).Append(",\n");
                    Indent(sb, 3).Append("\"allowParallel\": ").Append(Bool(m.AllowParallel)).Append(",\n");
                    Indent(sb, 3).Append("\"startedAtMs\": ").Append(Num(m.StartedAtMs)).Append(",\n");
                    Indent(sb, 3).Append("\"endedAtMs\": ").Append(Num(m.EndedAtMs)).Append(",\n");
                    Indent(sb, 3).Append("\"syncMs\": ").Append(Num(m.SyncMs)).Append(",\n");
                    Indent(sb, 3).Append("\"asyncMs\": ").Append(Num(m.AsyncMs)).Append(",\n");
                    Indent(sb, 3).Append("\"totalMs\": ").Append(Num(m.TotalMs)).Append(",\n");
                    Indent(sb, 3).Append("\"didRun\": ").Append(Bool(m.DidRun)).Append(",\n");
                    Indent(sb, 3).Append("\"isProblem\": ").Append(Bool(m.IsProblem)).Append(",\n");
                    Indent(sb, 3).Append("\"error\": ").Append(Str(m.Error)).Append('\n');
                    Indent(sb, 2).Append('}').Append(i < timeline.Modules.Count - 1 ? ",\n" : "\n");
                }
                Indent(sb, 1).Append("]\n");
            }

            sb.Append('}').Append('\n');

            var json = sb.ToString();

            if (!IsWellFormedJson(json, out var error))
                throw new InvalidOperationException(
                    $"Generated JSON failed validation and was not exported: {error}");

            return json;
        }

        // ── CSV ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Serialises the timeline to CSV: a "# Phase summary" block (one row per phase plus run
        /// totals), a blank separator, then a "# Modules" table with one row per module.
        /// </summary>
        internal static string ToCsv(LifecycleTimeline timeline)
        {
            if (timeline == null) throw new ArgumentNullException(nameof(timeline));

            var sb = new StringBuilder(4096);

            // ── Phase summary header block ──
            sb.Append("# Phase summary\n");
            sb.Append("Phase,StartedAtMs,EndedAtMs,DurationMs,ModuleCount,Batches,TimedOut\n");
            foreach (var p in timeline.Phases)
            {
                sb.Append(Csv(p.Phase.ToString())).Append(',')
                  .Append(Num(p.StartedAtMs)).Append(',')
                  .Append(Num(p.EndedAtMs)).Append(',')
                  .Append(Num(p.DurationMs)).Append(',')
                  .Append(p.ModuleCount.ToString(Inv)).Append(',')
                  .Append(p.Batches.ToString(Inv)).Append(',')
                  .Append(p.TimedOut ? "true" : "false").Append('\n');
            }

            // Run totals row.
            sb.Append(Csv("TOTAL")).Append(",,,")
              .Append(Num(timeline.TotalMs)).Append(',')
              .Append(timeline.Modules.Count.ToString(Inv)).Append(',')
              .Append(',')
              .Append(timeline.ProblemCount > 0 ? "problems=" + timeline.ProblemCount.ToString(Inv) : "")
              .Append('\n');

            sb.Append('\n');

            // ── Module table ──
            sb.Append("# Modules\n");
            sb.Append("DisplayName,Id,Phase,State,Level,SortIndex,IsAsync,AllowParallel,")
              .Append("StartedAtMs,EndedAtMs,SyncMs,AsyncMs,TotalMs,DidRun,IsProblem,Error\n");
            foreach (var m in timeline.Modules)
            {
                sb.Append(Csv(m.DisplayName)).Append(',')
                  .Append(Csv(m.Id != null ? m.Id.FullName : "")).Append(',')
                  .Append(Csv(m.Phase.ToString())).Append(',')
                  .Append(Csv(m.State.ToString())).Append(',')
                  .Append(m.Level.ToString(Inv)).Append(',')
                  .Append(m.SortIndex.ToString(Inv)).Append(',')
                  .Append(m.IsAsync ? "true" : "false").Append(',')
                  .Append(m.AllowParallel ? "true" : "false").Append(',')
                  .Append(Num(m.StartedAtMs)).Append(',')
                  .Append(Num(m.EndedAtMs)).Append(',')
                  .Append(Num(m.SyncMs)).Append(',')
                  .Append(Num(m.AsyncMs)).Append(',')
                  .Append(Num(m.TotalMs)).Append(',')
                  .Append(m.DidRun ? "true" : "false").Append(',')
                  .Append(m.IsProblem ? "true" : "false").Append(',')
                  .Append(Csv(m.Error ?? "")).Append('\n');
            }

            return sb.ToString();
        }

        // ── Formatting helpers ───────────────────────────────────────────────

        private static StringBuilder Indent(StringBuilder sb, int levels)
        {
            for (var i = 0; i < levels; i++) sb.Append("  ");
            return sb;
        }

        private static string Num(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return "null";
            return value.ToString("0.###", Inv);
        }

        private static string Bool(bool value) => value ? "true" : "false";

        private static string Str(string value)
        {
            if (value == null) return "null";

            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", Inv));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";

            var needsQuote = value.IndexOf(',') >= 0 || value.IndexOf('"') >= 0
                             || value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0;
            if (!needsQuote) return value;

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        // ── Minimal JSON well-formedness validator ───────────────────────────
        // A dependency-free structural check: verifies the generated text parses as a single JSON
        // value with balanced containers, valid literals, and correctly-escaped strings.

        private static bool IsWellFormedJson(string text, out string error)
        {
            var parser = new JsonScanner(text);
            error = null;
            try
            {
                parser.SkipWhitespace();
                parser.ParseValue();
                parser.SkipWhitespace();
                if (!parser.AtEnd)
                {
                    error = $"unexpected trailing content at index {parser.Position}";
                    return false;
                }
                return true;
            }
            catch (FormatException e)
            {
                error = e.Message;
                return false;
            }
        }

        private sealed class JsonScanner
        {
            private readonly string _s;
            private int _i;

            public JsonScanner(string s) { _s = s; _i = 0; }

            public int Position => _i;
            public bool AtEnd => _i >= _s.Length;

            public void SkipWhitespace()
            {
                while (_i < _s.Length)
                {
                    var c = _s[_i];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') _i++;
                    else break;
                }
            }

            public void ParseValue()
            {
                if (AtEnd) throw new FormatException("unexpected end of input");
                var c = _s[_i];
                switch (c)
                {
                    case '{': ParseObject(); break;
                    case '[': ParseArray(); break;
                    case '"': ParseString(); break;
                    case 't': Expect("true"); break;
                    case 'f': Expect("false"); break;
                    case 'n': Expect("null"); break;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) ParseNumber();
                        else throw new FormatException($"unexpected character '{c}' at index {_i}");
                        break;
                }
            }

            private void ParseObject()
            {
                _i++; // consume '{'
                SkipWhitespace();
                if (!AtEnd && _s[_i] == '}') { _i++; return; }
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _s[_i] != '"')
                        throw new FormatException($"expected object key at index {_i}");
                    ParseString();
                    SkipWhitespace();
                    if (AtEnd || _s[_i] != ':')
                        throw new FormatException($"expected ':' at index {_i}");
                    _i++;
                    SkipWhitespace();
                    ParseValue();
                    SkipWhitespace();
                    if (AtEnd) throw new FormatException("unterminated object");
                    var c = _s[_i];
                    if (c == ',') { _i++; continue; }
                    if (c == '}') { _i++; return; }
                    throw new FormatException($"expected ',' or '}}' at index {_i}");
                }
            }

            private void ParseArray()
            {
                _i++; // consume '['
                SkipWhitespace();
                if (!AtEnd && _s[_i] == ']') { _i++; return; }
                while (true)
                {
                    SkipWhitespace();
                    ParseValue();
                    SkipWhitespace();
                    if (AtEnd) throw new FormatException("unterminated array");
                    var c = _s[_i];
                    if (c == ',') { _i++; continue; }
                    if (c == ']') { _i++; return; }
                    throw new FormatException($"expected ',' or ']' at index {_i}");
                }
            }

            private void ParseString()
            {
                _i++; // consume opening quote
                while (_i < _s.Length)
                {
                    var c = _s[_i++];
                    if (c == '"') return;
                    if (c == '\\')
                    {
                        if (_i >= _s.Length) break;
                        var e = _s[_i++];
                        switch (e)
                        {
                            case '"': case '\\': case '/':
                            case 'b': case 'f': case 'n': case 'r': case 't':
                                break;
                            case 'u':
                                if (_i + 4 > _s.Length)
                                    throw new FormatException("truncated unicode escape");
                                for (var k = 0; k < 4; k++)
                                {
                                    if (!IsHex(_s[_i + k]))
                                        throw new FormatException($"invalid unicode escape at index {_i}");
                                }
                                _i += 4;
                                break;
                            default:
                                throw new FormatException($"invalid escape '\\{e}' at index {_i - 1}");
                        }
                    }
                    else if (c < 0x20)
                    {
                        throw new FormatException($"unescaped control character at index {_i - 1}");
                    }
                }
                throw new FormatException("unterminated string");
            }

            private void ParseNumber()
            {
                if (!AtEnd && _s[_i] == '-') _i++;
                if (AtEnd || !(_s[_i] >= '0' && _s[_i] <= '9'))
                    throw new FormatException($"invalid number at index {_i}");
                while (!AtEnd && _s[_i] >= '0' && _s[_i] <= '9') _i++;
                if (!AtEnd && _s[_i] == '.')
                {
                    _i++;
                    if (AtEnd || !(_s[_i] >= '0' && _s[_i] <= '9'))
                        throw new FormatException($"invalid fraction at index {_i}");
                    while (!AtEnd && _s[_i] >= '0' && _s[_i] <= '9') _i++;
                }
                if (!AtEnd && (_s[_i] == 'e' || _s[_i] == 'E'))
                {
                    _i++;
                    if (!AtEnd && (_s[_i] == '+' || _s[_i] == '-')) _i++;
                    if (AtEnd || !(_s[_i] >= '0' && _s[_i] <= '9'))
                        throw new FormatException($"invalid exponent at index {_i}");
                    while (!AtEnd && _s[_i] >= '0' && _s[_i] <= '9') _i++;
                }
            }

            private void Expect(string literal)
            {
                if (_i + literal.Length > _s.Length ||
                    string.CompareOrdinal(_s, _i, literal, 0, literal.Length) != 0)
                    throw new FormatException($"expected '{literal}' at index {_i}");
                _i += literal.Length;
            }

            private static bool IsHex(char c) =>
                (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }
    }

    /// <summary>Output format for <see cref="LifecycleTimelineExporter"/>.</summary>
    internal enum TimelineExportFormat
    {
        Json,
        Csv
    }
}
