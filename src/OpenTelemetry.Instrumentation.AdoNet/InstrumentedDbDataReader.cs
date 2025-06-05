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
    /// <summary>
    /// A <see cref="DbDataReader"/> implementation that wraps an underlying <see cref="DbDataReader"/>
    /// and is responsible for stopping the <see cref="Activity"/> created for the <see cref="DbCommand.ExecuteReader()"/> call.
    /// </summary>
    internal sealed class InstrumentedDbDataReader : DbDataReader
    {
        private readonly DbDataReader wrappedReader;
        private readonly Activity? activity; // Activity associated with the ExecuteReader command
        private readonly AdoNetInstrumentationOptions options;

        /// <summary>
        /// Initializes a new instance of the <see cref="InstrumentedDbDataReader"/> class.
        /// </summary>
        /// <param name="reader">The underlying <see cref="DbDataReader"/> to wrap. Must not be null.</param>
        /// <param name="activity">The <see cref="Activity"/> created for the ExecuteReader command. This can be null if the activity was not sampled.</param>
        /// <param name="options">The <see cref="AdoNetInstrumentationOptions"/> used by the parent command. Must not be null.</param>
        public InstrumentedDbDataReader(DbDataReader reader, Activity? activity, AdoNetInstrumentationOptions options)
        {
            this.wrappedReader = reader ?? throw new ArgumentNullException(nameof(reader));
            this.activity = activity; // This can be null if activity was not sampled
            this.options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// Stops the activity associated with this data reader.
        /// </summary>
        /// <param name="exception">Optional <see cref="Exception"/> to record on the activity.</param>
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
                // TODO: Consider adding Enrich option call for exceptions, e.g., this.options.EnrichWithException?.Invoke(this.activity, exception);
            }
            else
            {
                this.activity.SetStatus(ActivityStatusCode.Ok);
            }
            this.activity.Dispose();
        }

        // DbDataReader Overrides - mostly pass-through to wrappedReader
        /// <inheritdoc/>
        public override bool GetBoolean(int ordinal) => this.wrappedReader.GetBoolean(ordinal);
        /// <inheritdoc/>
        public override byte GetByte(int ordinal) => this.wrappedReader.GetByte(ordinal);
        /// <inheritdoc/>
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => this.wrappedReader.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);
        /// <inheritdoc/>
        public override char GetChar(int ordinal) => this.wrappedReader.GetChar(ordinal);
        /// <inheritdoc/>
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => this.wrappedReader.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);
        /// <inheritdoc/>
        public override string GetDataTypeName(int ordinal) => this.wrappedReader.GetDataTypeName(ordinal);
        /// <inheritdoc/>
        public override DateTime GetDateTime(int ordinal) => this.wrappedReader.GetDateTime(ordinal);
        /// <inheritdoc/>
        public override decimal GetDecimal(int ordinal) => this.wrappedReader.GetDecimal(ordinal);
        /// <inheritdoc/>
        public override double GetDouble(int ordinal) => this.wrappedReader.GetDouble(ordinal);
        /// <inheritdoc/>
        public override IEnumerator GetEnumerator() => this.wrappedReader.GetEnumerator();
        /// <inheritdoc/>
        public override Type GetFieldType(int ordinal) => this.wrappedReader.GetFieldType(ordinal);
        /// <inheritdoc/>
        public override float GetFloat(int ordinal) => this.wrappedReader.GetFloat(ordinal);
        /// <inheritdoc/>
        public override Guid GetGuid(int ordinal) => this.wrappedReader.GetGuid(ordinal);
        /// <inheritdoc/>
        public override short GetInt16(int ordinal) => this.wrappedReader.GetInt16(ordinal);
        /// <inheritdoc/>
        public override int GetInt32(int ordinal) => this.wrappedReader.GetInt32(ordinal);
        /// <inheritdoc/>
        public override long GetInt64(int ordinal) => this.wrappedReader.GetInt64(ordinal);
        /// <inheritdoc/>
        public override string GetName(int ordinal) => this.wrappedReader.GetName(ordinal);
        /// <inheritdoc/>
        public override int GetOrdinal(string name) => this.wrappedReader.GetOrdinal(name);
        /// <inheritdoc/>
        public override string GetString(int ordinal) => this.wrappedReader.GetString(ordinal);
        /// <inheritdoc/>
        public override object GetValue(int ordinal) => this.wrappedReader.GetValue(ordinal);
        /// <inheritdoc/>
        public override int GetValues(object[] values) => this.wrappedReader.GetValues(values);
        /// <inheritdoc/>
        public override bool IsDBNull(int ordinal) => this.wrappedReader.IsDBNull(ordinal);

        /// <inheritdoc/>
        public override int FieldCount => this.wrappedReader.FieldCount;
        /// <inheritdoc/>
        public override object this[int ordinal] => this.wrappedReader[ordinal];
        /// <inheritdoc/>
        public override object this[string name] => this.wrappedReader[name];
        /// <inheritdoc/>
        public override int RecordsAffected => this.wrappedReader.RecordsAffected;
        /// <inheritdoc/>
        public override bool HasRows => this.wrappedReader.HasRows;
        /// <inheritdoc/>
        public override bool IsClosed => this.wrappedReader.IsClosed;

        /// <inheritdoc/>
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

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public override int Depth => this.wrappedReader.Depth;

#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
        /// <inheritdoc/>
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

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public override Task CloseAsync()
        {
            StopActivity(); // Stop activity when reader is closed asynchronously
            return this.wrappedReader.CloseAsync();
        }

        /// <inheritdoc/>
        public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken) => this.wrappedReader.GetFieldValueAsync<T>(ordinal, cancellationToken);
        /// <inheritdoc/>
        public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken) => this.wrappedReader.IsDBNullAsync(ordinal, cancellationToken);
        /// <inheritdoc/>
        public override Stream GetStream(int ordinal) => this.wrappedReader.GetStream(ordinal);
        /// <inheritdoc/>
        public override TextReader GetTextReader(int ordinal) => this.wrappedReader.GetTextReader(ordinal);
#endif

        /// <inheritdoc/>
        public override DataTable? GetSchemaTable() => this.wrappedReader.GetSchemaTable();

        /// <summary>
        /// Closes the <see cref="DbDataReader"/> and stops the associated OpenTelemetry <see cref="Activity"/>.
        /// </summary>
        public override void Close()
        {
            StopActivity(); // Stop activity when reader is closed
            this.wrappedReader.Close();
        }

        /// <summary>
        /// Releases the resources used by the <see cref="InstrumentedDbDataReader"/> and stops the associated OpenTelemetry <see cref="Activity"/>.
        /// </summary>
        /// <param name="disposing"><see langword="true"/> to release both managed and unmanaged resources; <see langword="false"/> to release only unmanaged resources.</param>
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
