using Klemme15.Data;
using Klemme15.DataImport;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Klemme15.Tests.Import;

public class FullDatabaseImportTests
{
    [Fact]
    public void Extracts_measurement_tables_for_supported_variants_only()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"k15-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var sqlite = Path.Combine(dir, "bmw.sqlite");
        using (var conn = new SqliteConnection($"Data Source={sqlite};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE ecu_table (sgbd TEXT, name TEXT, rows INT, cols INT, columns TEXT);
                CREATE TABLE table_row (sgbd TEXT, "table" TEXT, idx INT, cells TEXT);
                INSERT INTO ecu_table VALUES ('msv70','MESSWERTETAB',2,12,'["ARG","ID","RESULTNAME","KOMMENTAR","LABEL","L/H","DATENTYP","NAME","ADD","MUL","EINHEIT","DIV"]');
                INSERT INTO table_row VALUES ('msv70','MESSWERTETAB',0,'["ITANS","0x4200","STAT_ANSAUGLUFTTEMPERATUR_WERT","Ansauglufttemperatur 1","TIA","-","unsigned char","-","-48","0,75","°C","1"]');
                INSERT INTO table_row VALUES ('msv70','MESSWERTETAB',1,'["-","0x4201","STAT_0x4201_WERT","Umgebungsdruck","AMP","-","unsigned integer","-","0","0,0829","hPa","1"]');
                INSERT INTO ecu_table VALUES ('zz_unused','MESSWERTETAB',0,2,'["ID","RESULTNAME"]');
                """;
            cmd.ExecuteNonQuery();
        }
        try
        {
            var db = VehicleDatabase.Load(Path.Combine(Path.GetTempPath(), "k15-none"));
            Assert.Equal(1, FullDatabaseImport.ImportTables(sqlite, db, dir));
            var tables = new MeasurementTables(dir);
            Assert.True(tables.Has("msv70"));
            var defs = tables.MeasurementsOf("msv70");
            Assert.Equal(2, defs.Count);
            Assert.Equal(0.75, defs[0].Mul);
            Assert.Equal(-48, defs[0].Add);
            Assert.Equal("°C", defs[0].Unit);
            Assert.Equal("unsigned integer", defs[1].DataType);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(dir, true);
        }
    }
}
