using System.IO;
using System.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OBS.Dashboard.Exporter.Configuration;
using OBS.Dashboard.Exporter.Extensions;
using OBS.Dashboard.Map.Models;
using OBS.Dashboard.Map.Services;
using OBS.Dashboard.Map.Services.API;
using OBS.Dashboard.Map.Services.Configuration;
using OBS.Stamm.Client.Api;
using AuthenticationService = OBS.Dashboard.Map.Services.AuthenticationService;

namespace OBS.Dashboard.Map
{
    /// <summary>
    /// Konfiguriert die Webanwendung und deren Services.
    /// </summary>
    public class Startup
    {
        /// <summary>
        /// IConfiguration wird verwendet, um Konfigurationsdaten
        /// aus verschiedenen Quellen zu laden, z. B.:
        ///     appsettings.json
        ///     Umgebungsvariablen
        ///     Kommandozeilenargumente
        ///     Geheimspeicher (Secrets Manager)
        /// </summary>
        public IConfiguration Configuration { get; }
        
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        /// <summary>
        /// ConfigureServices registriert die Services der Anwendung.
        /// </summary>
        /// <param name="services">Service Collection f�r Dependency Injection</param>
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddRazorPages();

            services.AddTransient<ICompanyInformationProvider, ConfigurationCompanyInformationProvider>();
            services.AddTransient<ICirclesInformationProvider, CirclesInformationProvider>();
            services.AddTransient<ICirclesPropertyProvider, ConfigurationCirclesPropertyProvider>();
            services.AddTransient<IEmployeeAddressesProvider, ConfigurationEmployeeAddressesProvider>();
            services.AddTransient<IEmployeeAddressesProvider, EmployeeObsStammAddressesProvider>();
            services.AddSingleton<HttpClient>();
            services.AddOptions();
            services.AddTransient<AuthenticationService>();

            // Laden der Konfigurationen aus appsettings.json
            services.Configure<ServicesConfiguration>(Configuration.GetSection("Services"));
            services.Configure<AuthenticationConfiguration>(Configuration.GetSection("Authentication"));

            /// <summary>
            /// Registriert die `IPersonsApi`-Implementierung als Transienten Dienst.
            /// Diese API erm�glicht den Zugriff auf die Personen-Daten aus dem OBS-Stamm-System.
            /// </summary>
            /// <remarks>
            /// - `AddTransient<IPersonsApi>` bedeutet, dass bei jeder Anforderung eine neue Instanz von `PersonsApi` erstellt wird.
            /// - Die `PersonsApi`-Instanz wird mit der `StammServiceUrl` aus der Konfiguration initialisiert.
            /// - Falls `StammServiceUrl` nicht gesetzt ist, wird eine alternative URL basierend auf `ServiceTemplateUrl` generiert.
            /// - Die Authentifizierung erfolgt �ber ein Access Token, das �ber den `AuthenticationService` geholt wird.
            /// - Die erhaltene `personsApi`-Instanz wird mit der authentifizierten Konfiguration versehen.
            /// </remarks>
            /// <param name="provider">Der Dienstanbieter, der f�r die Dependency Injection verwendet wird.</param>
            services.AddTransient<IPersonsApi>(provider =>
            {
                var servicesConfig = provider.GetRequiredService<IOptions<ServicesConfiguration>>();
                var obsStammUrl = servicesConfig.Value.StammServiceUrl ?? servicesConfig.Value.ServiceTemplateUrl.Interpolate("service", "obsstamm");
                var personsApi = new PersonsApi($"{obsStammUrl}");

                // Authentifizierung �ber AccessToken
                var authenticationService = provider.GetRequiredService<AuthenticationService>();
                var accessToken = authenticationService.GetAccessTokenAsync(default).Result;
                personsApi.Configuration = Stamm.Client.Client.Configuration.MergeConfigurations(
                    new Stamm.Client.Client.Configuration()
                    {
                        AccessToken = accessToken
                    },
                    personsApi.Configuration);

                return personsApi;
            });

            // Konfiguration des EmployeeCoordinatesProvider basierend auf appsettings.json
            if (Configuration.GetValue<CoordinateSource>("CoordinateSource") == CoordinateSource.Nominatim)
            {
                // Nominatim
                services.AddTransient<IEmployeeCoordinatesProvider, NominatimEmployeeCoordinateProvider>();
            }
            else
            {
                // appsettings.json
                services.AddTransient<IEmployeeCoordinatesProvider, ConfigurationEmployeeCoordinatesProvider>();
            }

            // Konfiguration des EmployeeAddressesProvider basierend auf appsettings.json
            if (Configuration.GetValue<EmployeeAddressesSource>("EmployeeAddressesSource") == EmployeeAddressesSource.OBSStamm)
            {
                // OBSStamm
                services.AddTransient<IEmployeeAddressesProvider, EmployeeObsStammAddressesProvider>();
            }
            else
            {
                // appsettings.json
                services.AddTransient<IEmployeeAddressesProvider, ConfigurationEmployeeAddressesProvider>();
            }
        }


        /// <summary>
        /// Konfiguriert die HTTP-Request-Pipeline der Anwendung.
        /// </summary>
        /// <remarks>
        /// Die HTTP-Request-Pipeline ist eine Kette von Middleware-Komponenten,
        /// die eine eingehende HTTP-Anfrage verarbeiten.
        /// </remarks>
        /// <param name="app">Anwendungsbuilder</param>
        /// <param name="env">Webhosting-Umgebung</param>
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            // Konfiguriert die Fehlerbehandlung basierend auf der Umgebung (Development/Production).
            if (env.IsDevelopment())
            {
                // Zeigt detaillierte Fehlerseiten an, wenn sich die Anwendung im Entwicklungsmodus befindet.
                app.UseDeveloperExceptionPage();
            }
            else
            {
                // Leitet auf eine generische Fehlerseite um, wenn ein Fehler in der Produktion auftritt.
                app.UseExceptionHandler("/Error");

                // Der Standard HSTS Wert betr�gt 30 Tage. Wenn diese in production scenarios ge�ndert werden soll, siehe: https://aka.ms/aspnetcore-hsts.
                // Erzwingt HTTPS-Verbindungen und sch�tzt vor Man-in-the-Middle-Angriffen.
                app.UseHsts();
            }

            app.UseHttpsRedirection(); // Leitet alle HTTP-Anfragen auf HTTPS um.
            app.UseRouting(); // Aktiviert die Routing-Funktionalit�t, die bestimmt, welche Endpunkte f�r Anfragen zust�ndig sind.
            app.UseAuthorization(); // Aktiviert die Autorisierung, um Zugriffskontrollen durchzuf�hren.

            // Konfiguriert die Endpunkte der Anwendung, z. B. Razor Pages oder API-Controller.
            app.UseEndpoints(endpoints =>
            {
                // Aktiviert Razor Pages als Endpunkte f�r die Webanwendung.
                endpoints.MapRazorPages();
            });
        }
    }
}
