// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics; // For Activity
using System.IO;          // For Stream
using System.Threading;
using System.Threading.Tasks;
using OpenTelemetry.Trace; // For ActivityStatusCode

namespace OpenTelemetry.Instrumentation.AdoNet
{
    internal sealed class InstrumentedDbDataReader : DbDataReader
    {
        private readonly DbDataReader wrappedReader;
        private readonly Activity? activity; // Activity associated with the ExecuteReader command
        private readonly AdoNetInstrumentationOptions options;

        public InstrumentedDbDataReader(DbDataReader reader, Activity? activity, AdoNetInstrumentationOptions options)
        {
            this.wrappedReader = reader ?? throw new ArgumentNullException(nameof(reader));
            this.activity = activity; // This can be null if activity was not sampled
            this.options = options ?? throw new ArgumentNullException(nameof(options));
        }

        private void StopActivity(Exception? exception = null)
        {
            if (this.activity == null)
            {
                return;
            }

            if (exception != null)
            {
                this.activity.SetStatus(ActivityStatusCode.Error, exception.Message);
                if (this.options.RecordException) // Use options from the command that created this reader
                {
                    this.activity.RecordException(exception);
                }
                // TODO: Enrich on exception
            }
            else
            {
                this.activity.SetStatus(ActivityStatusCode.Ok);
            }
            this.activity.Dispose();
        }

        // DbDataReader Overrides - mostly pass-through to wrappedReader
        public override bool GetBoolean(int ordinal) => this.wrappedReader.GetBoolean(ordinal);
        public override byte GetByte(int ordinal) => this.wrappedReader.GetByte(ordinal);
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => this.wrappedReader.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);
        public override char GetChar(int ordinal) => this.wrappedReader.GetChar(ordinal);
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => this.wrappedReader.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);
        public override string GetDataTypeName(int ordinal) => this.wrappedReader.GetDataTypeName(ordinal);
        public override DateTime GetDateTime(int ordinal) => this.wrappedReader.GetDateTime(ordinal);
        public override decimal GetDecimal(int ordinal) => this.wrappedReader.GetDecimal(ordinal);
        public override double GetDouble(int ordinal) => this.wrappedReader.GetDouble(ordinal);
        public override IEnumerator GetEnumerator() => this.wrappedReader.GetEnumerator();
        public override Type GetFieldType(int ordinal) => this.wrappedReader.GetFieldType(ordinal);
        public override float GetFloat(int ordinal) => this.wrappedReader.GetFloat(ordinal);
        public override Guid GetGuid(int ordinal) => this.wrappedReader.GetGuid(ordinal);
        public override short GetInt16(int ordinal) => this.wrappedReader.GetInt16(ordinal);
        public override int GetInt32(int ordinal) => this.wrappedReader.GetInt32(ordinal);
        public override long GetInt64(int ordinal) => this.wrappedReader.GetInt64(ordinal);
        public override string GetName(int ordinal) => this.wrappedReader.GetName(ordinal);
        public override int GetOrdinal(string name) => this.wrappedReader.GetOrdinal(name);
        public override string GetString(int ordinal) => this.wrappedReader.GetString(ordinal);
        public override object GetValue(int ordinal) => this.wrappedReader.GetValue(ordinal);
        public override int GetValues(object[] values) => this.wrappedReader.GetValues(values);
        public override bool IsDBNull(int ordinal) => this.wrappedReader.IsDBNull(ordinal);

        public override int FieldCount => this.wrappedReader.FieldCount;
        public override object this[int ordinal] => this.wrappedReader[ordinal];
        public override object this[string name] => this.wrappedReader[name];
        public override int RecordsAffected => this.wrappedReader.RecordsAffected;
        public override bool HasRows => this.wrappedReader.HasRows;
        public override bool IsClosed => this.wrappedReader.IsClosed;

        public override bool NextResult()
        {
            try
            {
                return this.wrappedReader.NextResult();
            }
            catch (Exception ex)
            {
                StopActivity(ex); // Stop activity if error occurs during NextResult
                throw;
            }
        }

        public override bool Read()
        {
            try
            {
                return this.wrappedReader.Read();
            }
            catch (Exception ex)
            {
                StopActivity(ex); // Stop activity if error occurs during Read
                throw;
            }
        }

        public override int Depth => this.wrappedReader.Depth;

#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
        public override Task<bool> ReadAsync(CancellationToken cancellationToken)
        {
            try
            {
                return this.wrappedReader.ReadAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                StopActivity(ex);
                throw;
            }
        }

        public override Task<bool> NextResultAsync(CancellationToken cancellationToken)
        {
            try
            {
                return this.wrappedReader.NextResultAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                StopActivity(ex);
                throw;
            }
        }

        public override Task CloseAsync()
        {
            StopActivity();
            return this.wrappedReader.CloseAsync();
        }

        public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken) => this.wrappedReader.GetFieldValueAsync<T>(ordinal, cancellationToken);
        public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken) => this.wrappedReader.IsDBNullAsync(ordinal, cancellationToken);
        public override Stream GetStream(int ordinal) => this.wrappedReader.GetStream(ordinal);
        public override TextReader GetTextReader(int ordinal) => this.wrappedReader.GetTextReader(ordinal);
#endif

        public override DataTable? GetSchemaTable() => this.wrappedReader.GetSchemaTable();

        // Crucial for stopping the activity
        public override void Close()
        {
            StopActivity();
            this.wrappedReader.Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopActivity(); // Ensure activity is stopped when reader is disposed
                this.wrappedReader.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
