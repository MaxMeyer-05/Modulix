using Microsoft.AspNetCore.Mvc;

namespace Modulix.Scanner.Tests.TestData;

#region Module Directory Test Data

/// <summary>
/// Provides invalid module directory paths.
/// </summary>
public class InvalidModuleDirectoryPathsTestData : TheoryData<string?>
{
    public InvalidModuleDirectoryPathsTestData()
    {
        Add(null);
        Add(string.Empty);
        Add("   ");
    }
}

#endregion

#region Controller Endpoint Test Data

/// <summary>
/// Provides controller route scenarios and their expected HTTP endpoints.
/// </summary>
public class ControllerEndpointTestData : TheoryData<string, string[]>
{
    public ControllerEndpointTestData()
    {
        Add("/SeparateActionRoute", ["GET /SeparateActionRoute/details"]);
        Add("/MultipleActionRoutes", ["GET /MultipleActionRoutes/first", "GET /MultipleActionRoutes/second"]);
        Add("/MultipleHttpMethods", ["GET /MultipleHttpMethods/details", "POST /MultipleHttpMethods/details"]);
        Add("/AbsoluteRoutes", ["GET /AbsoluteRoutes/details", "PUT /AbsoluteRoutes/tilde"]);
        Add("/InlineRoutes", ["GET /InlineRoutes/read", "POST /InlineRoutes/write"]);
        Add("/MixedRoutes", ["GET /MixedRoutes/shared", "POST /MixedRoutes/templated"]);
        Add("/SilentAndTemplated", ["GET /SilentAndTemplated", "POST /SilentAndTemplated/templated"]);
        Add("/BaseRoute", ["GET /BaseRoute"]);
        Add("/NoControllerRoute", ["GET /NoControllerRoute/details"]);
        Add("/TokenRoutes", ["GET /TokenRoutes/List"]);
        Add("/MultipleControllerRoutes", ["GET /MultipleControllerRoutes/v1/items", "GET /MultipleControllerRoutes/v2/items"]);
        Add("/DuplicateRoutes", ["GET /DuplicateRoutes/details", "POST /DuplicateRoutes/details"]);
        Add("/NormalizedRoutes", ["GET /NormalizedRoutes/details", "POST /NormalizedRoutes/details"]);
        Add("/AttributeOnly", ["GET /AttributeOnly/details"]);
        Add("/AbstractRoutes", []);
        Add("/NotAController", []);
        Add("/", ["GET /"]);
    }

    #region Controller Fixtures

    [Route("[controller]")]
    public class SeparateActionRouteController : ControllerBase
    {
        [HttpGet]
        [Route("details")]
        public void Details() { }
    }

    [Route("[controller]")]
    public class MultipleActionRoutesController : ControllerBase
    {
        [HttpGet]
        [Route("first")]
        [Route("second")]
        public void Details() { }
    }

    [Route("[controller]")]
    public class MultipleHttpMethodsController : ControllerBase
    {
        [HttpGet]
        [HttpPost]
        [Route("details")]
        public void Details() { }
    }

    [Route("unused/[controller]")]
    public class AbsoluteRoutesController : ControllerBase
    {
        [HttpGet]
        [Route("/[controller]/details")]
        public void Details() { }

        [HttpPut]
        [Route("~/[controller]/tilde")]
        public void TildeRoute() { }
    }

    [Route("[controller]")]
    public class InlineRoutesController : ControllerBase
    {
        [HttpGet("read")]
        [HttpPost("write")]
        public void Details() { }
    }

    [Route("[controller]")]
    public class MixedRoutesController : ControllerBase
    {
        [HttpGet]
        [Route("shared")]
        [HttpPost("templated")]
        public void Details() { }
    }

    [Route("[controller]")]
    public class SilentAndTemplatedController : ControllerBase
    {
        [HttpGet]
        [HttpPost("templated")]
        public void Details() { }
    }

    [Route("[controller]")]
    public class BaseRouteController : ControllerBase
    {
        [HttpGet]
        public void Details() { }
    }

    public class NoControllerRouteController : ControllerBase
    {
        [HttpGet("[controller]/details")]
        public void Details() { }
    }

    [Route("[CONTROLLER]")]
    public class TokenRoutesController : ControllerBase
    {
        [HttpGet("[ACTION]")]
        public void ListAsync() { }
    }

    [Route("[controller]/v1")]
    [Route("[controller]/v2")]
    public class MultipleControllerRoutesController : ControllerBase
    {
        [HttpGet("items")]
        public void Details() { }
    }

    [Route("[controller]")]
    [Route("[controller]/")]
    public class DuplicateRoutesController : ControllerBase
    {
        [HttpGet("details")]
        [HttpGet("details")]
        [HttpPost("details")]
        public void Details() { }
    }

    [Route("[controller]/")]
    public class NormalizedRoutesController : ControllerBase
    {
        [AcceptVerbs(" get ", "post")]
        [Route("details/")]
        public void Details() { }
    }

    [ApiController]
    [Route("[controller]")]
    public class AttributeOnlyController
    {
        [HttpGet("details")]
        public void Details() { }
    }

    [Route("[controller]")]
    public abstract class AbstractRoutesController : ControllerBase
    {
        [HttpGet("details")]
        public void Details() { }
    }

    public class NotAController
    {
        [HttpGet("/NotAController/details")]
        public void Details() { }
    }

    public class RootRouteController : ControllerBase
    {
        [HttpGet("")]
        public void Details() { }
    }

    #endregion
}

#endregion