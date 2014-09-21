using System.Web.Http;

namespace IdentityDISample.Controllers
{
    [Authorize]
    public class RetrieveDataController : ApiController
    {
        public object Get()
        {
            return new
            {
                SecretProperty = "This is a secret object",
                Name = "SecretObject"
            };
        }
    }
}
