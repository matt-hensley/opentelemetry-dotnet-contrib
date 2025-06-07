// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using OpenTelemetry.Trace; // For SemanticConventions constants

namespace OpenTelemetry.Instrumentation.AdoNet.Implementation
{
    internal static class DbSystemResolver
    {
        private static readonly ConcurrentDictionary<Type, string> Cache = new ConcurrentDictionary<Type, string>();

        // Using a case-insensitive comparer for full type names, though generally type names are case-sensitive in C#.
        // However, if there's any ambiguity or potential for different casing from GetType().FullName, this is safer.
        // For strict C# type names, Ordinal would be fine.
        private static readonly Dictionary<string, string> KnownProviderFullTypeNameMappings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "System.Data.SqlClient.SqlConnection", SemanticConventions.DbSystemMsSql },
            { "Microsoft.Data.SqlClient.SqlConnection", SemanticConventions.DbSystemMsSql },
            { "Npgsql.NpgsqlConnection", SemanticConventions.DbSystemPostgreSql },
            { "MySql.Data.MySqlClient.MySqlConnection", SemanticConventions.DbSystemMySql },
            { "MySqlConnector.MySqlConnection", SemanticConventions.DbSystemMySql },
            { "Oracle.ManagedDataAccess.Client.OracleConnection", SemanticConventions.DbSystemOracle },
            { "Microsoft.Data.Sqlite.SqliteConnection", SemanticConventions.DbSystemSqlite },
            { "System.Data.SQLite.SQLiteConnection", SemanticConventions.DbSystemSqlite },
            // IBM DB2 - Type names can vary based on specific driver package (Core, .NET Framework etc.)
            // Add more specific full names if known, e.g., "IBM.Data.Db2.Core.DB2Connection"
            // "IBM.Data.DB2.DB2Connection" is another common one.
            { "IBM.Data.DB2.DB2Connection", SemanticConventions.DbSystemDb2 },
            { "IBM.Data.DB2.Core.DB2Connection", SemanticConventions.DbSystemDb2 },
            { "IBM.Data.DB2.iSeries.DB2Connection", SemanticConventions.DbSystemDb2 }, // For iSeries
            { "FirebirdSql.Data.FirebirdClient.FbConnection", SemanticConventions.DbSystemFirebird }
        };

        public static string Resolve(DbConnection connection, string? configuredDbSystem)
        {
            if (!string.IsNullOrEmpty(configuredDbSystem))
            {
                return configuredDbSystem;
            }

            Type connectionType = connection.GetType();
            if (Cache.TryGetValue(connectionType, out var dbSystem))
            {
                return dbSystem;
            }

            dbSystem = ResolveInternal(connectionType);
            Cache.TryAdd(connectionType, dbSystem);
            return dbSystem;
        }

        private static string ResolveInternal(Type connectionType)
        {
            // Priority 1: Full Type Name Mapping
            string? typeFullName = connectionType.FullName;
            if (typeFullName != null && KnownProviderFullTypeNameMappings.TryGetValue(typeFullName, out var dbSystem))
            {
                return dbSystem;
            }

            // Priority 2: Heuristic based on simple type name (more resilient to version/assembly changes)
            string typeName = connectionType.Name;
            if (typeName.Contains("SqlConnection")) return SemanticConventions.DbSystemMsSql;
            if (typeName.Contains("NpgsqlConnection")) return SemanticConventions.DbSystemPostgreSql;
            if (typeName.Contains("MySqlConnection")) return SemanticConventions.DbSystemMySql;
            if (typeName.Contains("OracleConnection")) return SemanticConventions.DbSystemOracle;
            if (typeName.Contains("SqliteConnection")) return SemanticConventions.DbSystemSqlite;
            if (typeName.Contains("DB2Connection")) return SemanticConventions.DbSystemDb2; // Broad catch for DB2
            if (typeName.Contains("FbConnection")) return SemanticConventions.DbSystemFirebird;
            // Consider other common patterns if needed

            return "other"; // Default fallback
        }
    }
}
