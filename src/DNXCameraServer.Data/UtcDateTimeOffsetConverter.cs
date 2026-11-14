using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DNXCameraServer.Data;

/// <summary>
/// SQLite no admite ORDER BY sobre DateTimeOffset. Se persiste como texto ISO-8601
/// en UTC (ordenable lexicográficamente) y se materializa con un parse tolerante,
/// de modo que también se leen las filas escritas por el mapeo por defecto de EF.
/// </summary>
public sealed class UtcDateTimeOffsetConverter : ValueConverter<DateTimeOffset, string>
{
    private const string Format = "yyyy-MM-dd HH:mm:ss.fffffffzzz";

    public UtcDateTimeOffsetConverter() : base(
        value => value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture),
        text => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))
    {
    }
}
