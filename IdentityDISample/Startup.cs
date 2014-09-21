using Microsoft.Owin;
using Owin;

[assembly: OwinStartupAttribute(typeof(IdentityDISample.Startup))]
namespace IdentityDISample
{
    public partial class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            ConfigureAuth(app);

            var apiConfig = ConfigureWebApi();
            ConfigureDependencyInjection(app, apiConfig);
            app.UseWebApi(apiConfig);
        }
    }
}
