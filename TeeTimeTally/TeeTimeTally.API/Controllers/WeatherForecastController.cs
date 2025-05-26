using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TeeTimeTally.Shared.Auth;

namespace TeeTimeTally.API.Controllers;

[ApiController]
[Route("/api/courses")]
[Authorize(Policy = Auth0Scopes.ManageRoundCth)]
public class WeatherForecastController() : ControllerBase
{
	[HttpGet(Name = "GetWeatherForecast")]
	public IEnumerable<Course> Get()
	{
		return Enumerable.Range(1, 5).Select(index => new Course
		{
			Id = index,
			CTHHole = 2,
			Name= "Mape"
		})
		.ToArray();
	}
}
