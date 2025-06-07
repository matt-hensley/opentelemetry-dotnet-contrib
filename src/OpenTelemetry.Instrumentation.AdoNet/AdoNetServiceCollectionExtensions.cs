// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenTelemetry.Instrumentation.AdoNet; // Namespace for AdoNetInstrumentationOptions, InstrumentedDbProviderFactory
using OpenTelemetry.Internal; // For Guard

namespace Microsoft.Extensions.DependencyInjection // Standard namespace for IServiceCollection extensions
{
    /// <summary>
    /// Extension methods for setting up ADO.NET instrumentation in an <see cref="IServiceCollection" />.
    /// </summary>
    public static class AdoNetServiceCollectionExtensions
    {
        /// <summary>
        /// Configures default <see cref="AdoNetInstrumentationOptions"/> for ADO.NET instrumentation.
        /// </summary>
        /// <remarks>
        /// This method registers an <see cref="IConfigureOptions{AdoNetInstrumentationOptions}"/> action
        /// to configure the default <see cref="AdoNetInstrumentationOptions"/> instance (options name: <see cref="Options.DefaultName"/>).
        /// These options are resolved by <see cref="AddInstrumentedDbProviderFactory(IServiceCollection, string, string, ServiceLifetime)"/>
        /// and <see cref="AddInstrumentedDbConnection(IServiceCollection, Func{IServiceProvider, DbConnection}, string, ServiceLifetime)"/>
        /// when no specific options name is provided to those methods.
        /// The <see cref="Otel.TracerProviderBuilderExtensions.AddAdoNetInstrumentation(Otel.TracerProviderBuilder, Action{AdoNetInstrumentationOptions})"/>
        /// method also sets global default options but does not directly use DI-configured options.
        /// </remarks>
        /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
        /// <param name="configureOptions">A delegate to configure the default <see cref="AdoNetInstrumentationOptions"/>.</param>
        /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
        public static IServiceCollection ConfigureAdoNetInstrumentation(
            this IServiceCollection services,
            Action<AdoNetInstrumentationOptions> configureOptions)
        {
            Guard.ThrowIfNull(services);
            Guard.ThrowIfNull(configureOptions);

            services.Configure(Options.DefaultName, configureOptions);
            return services;
        }

        /// <summary>
        /// Configures a named <see cref="AdoNetInstrumentationOptions"/> instance for ADO.NET instrumentation.
        /// </summary>
        /// <remarks>
        /// This method registers an <see cref="IConfigureOptions{AdoNetInstrumentationOptions}"/> action
        /// to configure a named <see cref="AdoNetInstrumentationOptions"/> instance.
        /// These named options can be resolved by <see cref="AddInstrumentedDbProviderFactory(IServiceCollection, string, string, ServiceLifetime)"/>
        /// and <see cref="AddInstrumentedDbConnection(IServiceCollection, Func{IServiceProvider, DbConnection}, string, ServiceLifetime)"/>
        /// by providing the same <paramref name="name"/> to those methods.
        /// </remarks>
        /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
        /// <param name="name">The name of the options instance to configure.</param>
        /// <param name="configureOptions">A delegate to configure the named <see cref="AdoNetInstrumentationOptions"/>.</param>
        /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
        public static IServiceCollection ConfigureAdoNetInstrumentation(
            this IServiceCollection services,
            string name,
            Action<AdoNetInstrumentationOptions> configureOptions)
        {
            Guard.ThrowIfNull(services);
            Guard.ThrowIfNull(name);
            Guard.ThrowIfNull(configureOptions);

            services.Configure(name, configureOptions);
            // Also ensure a default AdoNetInstrumentationOptions is available if requested by name,
            // or if generic IOptions<AdoNetInstrumentationOptions> is resolved.
            // This ensures that AddOptions is called, registering IOptions<AdoNetInstrumentationOptions> etc.
            services.AddOptions<AdoNetInstrumentationOptions>(name);

            return services;
        }

        /// <summary>
        /// Registers an instrumented <see cref="DbProviderFactory"/> for the specified provider invariant name,
        /// replacing the standard <see cref="DbProviderFactory"/> registration for that provider if resolved via <c>typeof(DbProviderFactory)</c>
        /// and this is the last registration.
        /// </summary>
        /// <remarks>
        /// The underlying <see cref="DbProviderFactory"/> is obtained using <see cref="DbProviderFactories.GetFactory(string)"/>.
        /// The specified <paramref name="optionsName"/> is used to retrieve <see cref="AdoNetInstrumentationOptions"/>
        /// from the DI container (configured via <see cref="ConfigureAdoNetInstrumentation(IServiceCollection, string, Action{AdoNetInstrumentationOptions})"/> or its default overload).
        /// This method registers the instrumented factory as <c>typeof(DbProviderFactory)</c>. If an application resolves <c>DbProviderFactory</c>
        /// and expects a specific non-instrumented factory, or uses multiple provider factories, care should be taken with registration order or
        /// alternative resolution strategies.
        /// </remarks>
        /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
        /// <param name="providerInvariantName">The provider invariant name of the <see cref="DbProviderFactory"/> to instrument (e.g., "System.Data.SqlClient").</param>
        /// <param name="optionsName">The name of the <see cref="AdoNetInstrumentationOptions"/> instance to use for this factory. Defaults to <see cref="Options.DefaultName"/>.</param>
        /// <param name="lifetime">The <see cref="ServiceLifetime"/> of the instrumented factory. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
        /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
        public static IServiceCollection AddInstrumentedDbProviderFactory(
            this IServiceCollection services,
            string providerInvariantName,
            string? optionsName = null,
            ServiceLifetime lifetime = ServiceLifetime.Singleton)
        {
            Guard.ThrowIfNull(services);
            Guard.ThrowIfNullOrEmpty(providerInvariantName);
            optionsName ??= Options.DefaultName;

            services.AddOptions<AdoNetInstrumentationOptions>(optionsName);

            services.Add(new ServiceDescriptor(
                typeof(DbProviderFactory),
                sp => {
                    var underlyingFactory = DbProviderFactories.GetFactory(providerInvariantName);
                    var options = sp.GetRequiredService<IOptionsMonitor<AdoNetInstrumentationOptions>>().Get(optionsName);
                    return new InstrumentedDbProviderFactory(underlyingFactory, options);
                },
                lifetime));

            return services;
        }


        /// <summary>
        /// Registers a delegate that, when resolved, provides an instrumented <see cref="DbConnection"/>.
        /// </summary>
        /// <remarks>
        /// The <paramref name="originalConnectionFactory"/> is a delegate you provide to create your base <see cref="DbConnection"/> instance (e.g., a <c>new SqlConnection(connectionString)</c>).
        /// This factory will be called, and its resulting connection will be wrapped by <see cref="AdoNetInstrumentation.InstrumentConnection(DbConnection, AdoNetInstrumentationOptions)"/>.
        /// The specified <paramref name="optionsName"/> is used to retrieve <see cref="AdoNetInstrumentationOptions"/>
        /// from the DI container (configured via <see cref="ConfigureAdoNetInstrumentation(IServiceCollection, string, Action{AdoNetInstrumentationOptions})"/> or its default overload).
        /// The instrumented connection is registered as <c>typeof(DbConnection)</c>.
        /// </remarks>
        /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
        /// <param name="originalConnectionFactory">A delegate that creates an instance of the original <see cref="DbConnection"/> (e.g., <c>sp => new SqliteConnection(sp.GetRequiredService&lt;IConfiguration&gt;().GetConnectionString("Default"))</c>).</param>
        /// <param name="optionsName">The name of the <see cref="AdoNetInstrumentationOptions"/> instance to use for this connection. Defaults to <see cref="Options.DefaultName"/>.</param>
        /// <param name="lifetime">The <see cref="ServiceLifetime"/> of the instrumented connection. Defaults to <see cref="ServiceLifetime.Scoped"/>.</param>
        /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
        public static IServiceCollection AddInstrumentedDbConnection(
            this IServiceCollection services,
            Func<IServiceProvider, DbConnection> originalConnectionFactory,
            string? optionsName = null,
            ServiceLifetime lifetime = ServiceLifetime.Scoped)
        {
            Guard.ThrowIfNull(services);
            Guard.ThrowIfNull(originalConnectionFactory);
            optionsName ??= Options.DefaultName;

            services.AddOptions<AdoNetInstrumentationOptions>(optionsName);

            var descriptor = new ServiceDescriptor(
                typeof(DbConnection),
                sp =>
                {
                    var originalConnection = originalConnectionFactory(sp);
                    var options = sp.GetRequiredService<IOptionsMonitor<AdoNetInstrumentationOptions>>().Get(optionsName);
                    return AdoNetInstrumentation.InstrumentConnection(originalConnection, options);
                },
                lifetime);

            services.Add(descriptor);
            return services;
        }
    }
}
