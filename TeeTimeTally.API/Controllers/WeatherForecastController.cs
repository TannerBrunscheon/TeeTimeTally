using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using TeeTimeTally.Shared.Auth; // For Auth0Scopes

namespace TeeTimeTally.API.Endpoints.Courses;

[Authorize(Policy = Auth0Scopes.ManageRoundCth)]
public class GetCoursesEndpoint : EndpointWithoutRequest<IEnumerable<Course>>
{
	public override void Configure()
	{
		Get("/courses"); 
	}

	public override async Task HandleAsync(CancellationToken ct)
	{
		// This logic replicates the Get() method from your WeatherForecastController
		var courses = Enumerable.Range(1, 5).Select(index => new Course
		{
			Id = index,
			CTHHole = 2, // Matches your example
			Name = "Mape"  // Matches your example
		}).ToArray();

		await SendAsync(courses, cancellation: ct);
	}
}