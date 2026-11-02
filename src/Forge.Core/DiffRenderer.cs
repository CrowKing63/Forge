using DiffPlex;
using DiffPlex.Model;

namespace Forge.Core;

/// <summary>
/// The kind of change a diff segment represents. For <see cref="DiffSegmentKind.Equal"/>
/// segments <see cref="DiffSegment.OriginalText"/> and <see cref="DiffSegment.ResultText"/>
/// are identical.
/// </summary>
public enum DiffSegmentKind
{
    Equal,
    Inserted,
    Removed,
}

/// <summary>
/// One contiguous region of the original/result pair. Exactly one side is empty for
/// <see cref="DiffSegmentKind.Inserted"/> (result-only text) and
/// <see cref="DiffSegmentKind.Removed"/> (original-only text).
/// </summary>
public sealed record DiffSegment(DiffSegmentKind Kind, string OriginalText, string ResultText);

/// <summary>
/// Computes a segment-level diff between the original text and the model result.
///
/// Pure and deterministic: no I/O, same input → same output. Backed by DiffPlex's
/// word-level differ: its <see cref="DiffResult.DiffBlocks"/> index into tiled
/// piece arrays, which are folded here into ordered segments. The UI (S7) renders
/// these segments: the result pane highlights <see cref="DiffSegmentKind.Inserted"/>
/// parts, the original pane highlights <see cref="DiffSegmentKind.Removed"/> parts.
/// </summary>
public static class DiffRenderer
{
    /// <summary>Word separators for the diff (whitespace, matching the text Forge handles).</summary>
    private static readonly char[] WordSeparators = { ' ', '\t', '\n', '\r', '\f' };

    /// <summary>
    /// Splits (original, result) into ordered segments. Concatenating all
    /// <see cref="DiffSegment.OriginalText"/> values reproduces <paramref name="original"/>,
    /// and all <see cref="DiffSegment.ResultText"/> values reproduce <paramref name="result"/>.
    /// </summary>
    public static IReadOnlyList<DiffSegment> Compute(string original, string result)
    {
        var differ = new Differ();
        var diff = differ.CreateWordDiffs(original ?? string.Empty, result ?? string.Empty, ignoreWhitespace: false, separators: WordSeparators);

        var segments = new List<DiffSegment>();
        int oldEnd = 0;
        int newEnd = 0;
        foreach (var block in diff.DiffBlocks)
        {
            AddEqual(segments, diff.PiecesOld, oldEnd, block.DeleteStartA, diff.PiecesNew, newEnd, block.InsertStartB);
            if (block.DeleteCountA > 0)
                segments.Add(new DiffSegment(DiffSegmentKind.Removed, Concat(diff.PiecesOld, block.DeleteStartA, block.DeleteCountA), string.Empty));
            if (block.InsertCountB > 0)
                segments.Add(new DiffSegment(DiffSegmentKind.Inserted, string.Empty, Concat(diff.PiecesNew, block.InsertStartB, block.InsertCountB)));

            oldEnd = block.DeleteStartA + block.DeleteCountA;
            newEnd = block.InsertStartB + block.InsertCountB;
        }

        AddEqual(segments, diff.PiecesOld, oldEnd, diff.PiecesOld.Length, diff.PiecesNew, newEnd, diff.PiecesNew.Length);
        return segments;
    }

    /// <summary>Appends one equal segment pairing the old and new gap ranges.</summary>
    private static void AddEqual(List<DiffSegment> segments, string[] oldPieces, int oldStart, int oldEnd, string[] newPieces, int newStart, int newEnd)
    {
        if (oldEnd > oldStart || newEnd > newStart)
            segments.Add(new DiffSegment(
                DiffSegmentKind.Equal,
                Concat(oldPieces, oldStart, oldEnd - oldStart),
                Concat(newPieces, newStart, newEnd - newStart)));
    }

    private static string Concat(string[] pieces, int start, int count) =>
        string.Concat(pieces.Skip(start).Take(count));
}
