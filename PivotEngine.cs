using System;
using System.Diagnostics;
using System.IO;
using System.Data;
using Microsoft.Data.Sqlite;
using DuckDB.NET.Data;
using System.Text;

namespace LogisticsZero {
    public class PivotEngine {
        private const string SqliteDbPath = "vault.db";

        public void InitializeDatabase() {
            var sqliteConnectionString = $"Data Source={SqliteDbPath}";
            using (var conn = new SqliteConnection(sqliteConnectionString)) {
                conn.Open();
                
                // Configure write-ahead logging (WAL) for concurrent reads/writes
                using (var cmd = conn.CreateCommand()) {
                    cmd.CommandText = "PRAGMA journal_mode=WAL;";
                    cmd.ExecuteNonQuery();
                }

                // Initialize a generic inventory schema
                using (var cmd = conn.CreateCommand()) {
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS inventory_items (
                            id INTEGER PRIMARY KEY,
                            category TEXT,
                            name TEXT,
                            price REAL,
                            depreciation_rate REAL
                        );";
                    cmd.ExecuteNonQuery();
                }

                // Check and seed 10,000 mock rows if database is empty
                long count = 0;
                using (var cmd = conn.CreateCommand()) {
                    cmd.CommandText = "SELECT COUNT(*) FROM inventory_items;";
                    count = (long)cmd.ExecuteScalar();
                }

                if (count == 0) {
                    SeedMockData(conn);
                }
            }
        }

        private void SeedMockData(SqliteConnection conn) {
            string[] categories = { "Hardware", "Furniture", "Electronics", "Office Supplies", "Networking" };
            string[] items = { "Laptop", "Desk Chair", "Monitor", "Paper Shredder", "Router" };
            Random rand = new Random(42);

            using (var transaction = conn.BeginTransaction()) {
                using (var cmd = conn.CreateCommand()) {
                    cmd.CommandText = @"
                        INSERT INTO inventory_items (category, name, price, depreciation_rate)
                        VALUES ($cat, $name, $price, $rate);";
                    
                    var catParam = cmd.CreateParameter();
                    catParam.ParameterName = "$cat";
                    cmd.Parameters.Add(catParam);

                    var nameParam = cmd.CreateParameter();
                    nameParam.ParameterName = "$name";
                    cmd.Parameters.Add(nameParam);

                    var priceParam = cmd.CreateParameter();
                    priceParam.ParameterName = "$price";
                    cmd.Parameters.Add(priceParam);

                    var rateParam = cmd.CreateParameter();
                    rateParam.ParameterName = "$rate";
                    cmd.Parameters.Add(rateParam);

                    for (int i = 0; i < 10000; i++) {
                        catParam.Value = categories[rand.Next(categories.Length)];
                        nameParam.Value = items[rand.Next(items.Length)];
                        priceParam.Value = 100.0 + rand.NextDouble() * 5000.0;
                        rateParam.Value = 0.05 + rand.NextDouble() * 0.15;
                        cmd.ExecuteNonQuery();
                    }
                }
                transaction.Commit();
            }
        }

        // Runs any arbitrary SQL query via DuckDB over the SQLite database file
        public string ExecuteOlapQuery(string sqlQuery) {
            var stopwatch = Stopwatch.StartNew();
            var jsonResult = new StringBuilder();

            try {
                using (var duckConn = new DuckDBConnection("Data Source=:memory:")) {
                    duckConn.Open();
                    
                    using (var cmd = duckConn.CreateCommand()) {
                        cmd.CommandText = "INSTALL sqlite; LOAD sqlite;";
                        cmd.ExecuteNonQuery();

                        // Rewrite the query if it targets the local table to wrap it inside sqlite_scan
                        string secureQuery = sqlQuery.Replace("inventory_items", $"sqlite_scan('{SqliteDbPath}', 'inventory_items')");
                        cmd.CommandText = secureQuery;

                        using (var reader = cmd.ExecuteReader()) {
                            var dt = new DataTable();
                            dt.Load(reader);

                            // Convert DataTable to simple JSON array
                            jsonResult.Append("[");
                            for (int i = 0; i < dt.Rows.Count; i++) {
                                jsonResult.Append("{");
                                for (int j = 0; j < dt.Columns.Count; j++) {
                                    string colName = dt.Columns[j].ColumnName;
                                    object val = dt.Rows[i][j];
                                    
                                    jsonResult.Append($"\"{colName}\":");
                                    if (val is DBNull) {
                                        jsonResult.Append("null");
                                    } else if (val is string || val is DateTime) {
                                        jsonResult.Append($"\"{val.ToString().Replace("\"", "\\\"")}\"");
                                    } else if (val is bool b) {
                                        jsonResult.Append(b ? "true" : "false");
                                    } else {
                                        jsonResult.Append(val.ToString());
                                    }

                                    if (j < dt.Columns.Count - 1) jsonResult.Append(",");
                                }
                                jsonResult.Append("}");
                                if (i < dt.Rows.Count - 1) jsonResult.Append(",");
                            }
                            jsonResult.Append("]");
                        }
                    }
                }
            } catch (Exception ex) {
                return $"{{\"error\": \"{ex.Message.Replace("\"", "\\\"")}\"}}";
            }

            stopwatch.Stop();
            // Return raw JSON data and prepend the execution time header
            return $"{stopwatch.Elapsed.TotalMilliseconds:F3}|{jsonResult.ToString()}";
        }
    }
}
