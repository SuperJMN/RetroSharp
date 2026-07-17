namespace RetroSharp.FunctionalAcceptance;

internal static class ActorSpawnRomTableFixture
{
    private static readonly string[] SpawnInitialFields =
        ["active", "vx", "vy", "state", "timer", "facing", "animTick", "health"];

    public static IReadOnlyDictionary<string, byte[]> ExpectedColumns(int recordCount)
    {
        var columns = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["kind"] = new byte[recordCount],
            ["x"] = new byte[recordCount],
            ["xHi"] = new byte[recordCount],
            ["y"] = new byte[recordCount],
            ["yHi"] = new byte[recordCount],
            ["active"] = new byte[recordCount],
            ["vx"] = new byte[recordCount],
            ["vy"] = new byte[recordCount],
            ["state"] = new byte[recordCount],
            ["timer"] = new byte[recordCount],
            ["facing"] = new byte[recordCount],
            ["animTick"] = new byte[recordCount],
            ["health"] = new byte[recordCount],
        };
        for (var index = 0; index < recordCount; index++)
        {
            var x = index * 257;
            var y = index * 257;
            columns["kind"][index] = (byte)(index % 2 + 1);
            columns["x"][index] = (byte)x;
            columns["xHi"][index] = (byte)(x >> 8);
            columns["y"][index] = (byte)y;
            columns["yHi"][index] = (byte)(y >> 8);
            for (var field = 0; field < SpawnInitialFields.Length; field++)
            {
                columns[SpawnInitialFields[field]][index] = InitialValue(index, field);
            }
        }

        return columns;
    }

    public static string ObjectsJson(int recordCount) =>
        "[" + string.Join(",", Enumerable.Range(0, recordCount).Select(index =>
        {
            var properties = string.Join(",", SpawnInitialFields.Select((field, fieldIndex) =>
                $"{{\"name\":\"{field}\",\"type\":\"int\",\"value\":{InitialValue(index, fieldIndex)}}}"));
            return $"{{\"id\":{index + 1},\"type\":\"{(index % 2 == 0 ? "Goomba" : "Bat")}\",\"x\":{index * 257},\"y\":{index * 257},\"properties\":[{properties}]}}";
        })) + "]";

    public static string SpacedObjectsJson(int recordCount, int spacing) =>
        "[" + string.Join(",", Enumerable.Range(0, recordCount).Select(index =>
            $"{{\"id\":{index + 1},\"type\":\"Goomba\",\"x\":{index * spacing},\"y\":40}}")) + "]";

    public static string ActorSource(bool includeReturn) =>
        $$"""
          void Main() {
              Actors.Pool(enemies, 1);
              Enemies.Def(Goomba, behavior: Walker);
              Enemies.Def(Bat, behavior: Flyer);
              Actors.SpawnWindow(enemies, "level.tmj", "actors", 0, 1);
              {{(includeReturn ? "return;" : string.Empty)}}
          }
          """;

    public static string TableSource(IReadOnlyDictionary<string, byte[]> columns, bool includeReturn) =>
        LookupSource(columns, includeReturn, column => "0");

    public static string LadderSource(IReadOnlyDictionary<string, byte[]> columns, bool includeReturn) =>
        LookupSource(columns, includeReturn, column =>
        {
            var expression = column.Value[^1].ToString();
            for (var index = column.Value.Length - 2; index >= 0; index--)
            {
                expression = $"index == {index} ? {column.Value[index]} : {expression}";
            }

            return expression;
        });

    public static int CountMaskedSequences(byte[] bytes, IReadOnlyList<byte?> pattern)
    {
        var count = 0;
        for (var offset = 0; offset <= bytes.Length - pattern.Count; offset++)
        {
            if (pattern.Select((value, index) => !value.HasValue || value.Value == bytes[offset + index]).All(matches => matches))
            {
                count++;
            }
        }

        return count;
    }

    private static string LookupSource(
        IReadOnlyDictionary<string, byte[]> columns,
        bool includeReturn,
        Func<KeyValuePair<string, byte[]>, string> expression)
    {
        var functions = string.Join(Environment.NewLine, columns.Select(column =>
            $"inline u8 table_{column.Key}(u8 index) => {expression(column)};"));
        var reads = string.Join(Environment.NewLine, columns.Keys.Select((field, index) =>
            $"u8 result{index} = table_{field}(index);"));
        return $$"""
                 {{functions}}

                 void Main() {
                     u8 index = 0;
                     {{reads}}
                     {{(includeReturn ? "return;" : string.Empty)}}
                 }
                 """;
    }

    private static byte InitialValue(int index, int fieldIndex) =>
        (byte)((index + fieldIndex + 1) % 255);
}
