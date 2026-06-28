using System;
using System.Diagnostics;
using System.IO;
using System.Data;
using Microsoft.Data.Sqlite;
using DuckDB.NET.Data;

namespace LogisticsZero {
    public class PivotEngine {
        private const string SqliteDbPath = "vault.db";

        public void InitializeDatabase() {
            // 1. Initialize SQLite vault in WAL mode
            var sqliteConnectionString = $"Data Source={SqliteDbPath}";
            using (var conn = new SqliteConnection(sqliteConnectionString)) {
                conn.Open();
                
                // Set WAL journal mode to support high-concurrency writes
                using (var cmd = conn.CreateCommand()) {
                    cmd.CommandText = "PRAGMA journal_mode=WAL;";
                    cmd.ExecuteNonQuery();
                }

                // Create the KSAU-HS asset inventory table
                using (var cmd = conn.CreateCommand()) {
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS financial_assets (
                            id INTEGER PRIMARY KEY,
                            department TEXT,
                            equipment TEXT,
                            cost REAL,
                            depreciation_rate REAL
                        );";
                    cmd.ExecuteNonQuery();
                }

                // Count rows; if empty, seed with 10,000+ records
                long count = 0;
                using (var cmd = conn.CreateCommand()) {
                    cmd.CommandText = "SELECT COUNT(*) FROM financial_assets;";
                    count = (long)cmd.ExecuteScalar();
                }

                if (count == 0) {
                    SeedMockData(conn);
                }
            }
        }

        private void SeedMockData(SqliteConnection conn) {
            string[] departments = { "Cardiology", "Neurology", "Radiology", "Surgery", "Pediatrics" };
            string[] equipment = { "MRI Scanner", "X-Ray Machine", "Ultrasound", "CT Scanner", "Defibrillator" };
            Random rand = new Random(42);

            using (var transaction = conn.BeginTransaction()) {
                using (var cmd = conn.CreateCommand()) {
                    cmd.CommandText = @"
                        INSERT INTO financial_assets (department, equipment, cost, depreciation_rate)
                        VALUES ($dept, $equip, $cost, $rate);";
                    
                    var deptParam = cmd.CreateParameter();
                    deptParam.ParameterName = "$dept";
                    cmd.Parameters.Add(deptParam);

                    var equipParam = cmd.CreateParameter();
                    equipParam.ParameterName = "$equip";
                    cmd.Parameters.Add(equipParam);

                    var costParam = cmd.CreateParameter();
                    costParam.ParameterName = "$cost";
                    cmd.Parameters.Add(costParam);

                    var rateParam = cmd.CreateParameter();
                    rateParam.ParameterName = "$rate";
                    cmd.Parameters.Add(rateParam);

                    for (int i = 0; i < 10000; i++) {
                        deptParam.Value = departments[rand.Next(departments.Length)];
                        equipParam.Value = equipment[rand.Next(equipment.Length)];
                        costParam.Value = 5000.0 + rand.NextDouble() * 950000.0;
                        rateParam.Value = 0.05 + rand.NextDouble() * 0.15;
                        cmd.ExecuteNonQuery();
                    }
                }
                transaction.Commit();
            }
        }

        public (double totalCost, double avgDepreciation, double executionTimeMs) AggregateByDepartment(string department) {
            var stopwatch = Stopwatch.StartNew();
            double totalCost = 0.0;
            double avgDepreciation = 0.0;

            // DuckDB.NET queries the local SQLite file directly via its SQLite Scanner extension
            using (var duckConn = new DuckDBConnection("Data Source=:memory:")) {
                duckConn.Open();
                
                using (var cmd = duckConn.CreateCommand()) {
                    // Install and load the SQLite extension in DuckDB
                    cmd.CommandText = "INSTALL sqlite; LOAD sqlite;";
                    cmd.ExecuteNonQuery();

                    // Read directly from the SQLite vault using sqlite_scan for O(1) aggregations
                    cmd.CommandText = $@"
                        SELECT SUM(cost), AVG(depreciation_rate)
                        FROM sqlite_scan('{SqliteDbPath}', 'financial_assets')
                        WHERE department = $1;";
                    
                    var param = cmd.CreateParameter();
                    param.Value = department;
                    cmd.Parameters.Add(param);

                    using (var reader = cmd.ExecuteReader()) {
                        if (reader.Read()) {
                            totalCost = reader.IsDBNull(0) ? 0.0 : reader.GetDouble(0);
                            avgDepreciation = reader.IsDBNull(1) ? 0.0 : reader.GetDouble(1);
                        }
                    }
                }
            }

            stopwatch.Stop();
            return (totalCost, avgDepreciation, stopwatch.Elapsed.TotalMilliseconds);
        }
    }
}
