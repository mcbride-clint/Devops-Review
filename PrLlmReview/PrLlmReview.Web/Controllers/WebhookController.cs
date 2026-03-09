using Microsoft.AspNetCore.Mvc;
using PrLlmReview.BackgroundServices;
using PrLlmReview.Models;

namespace PrLlmReview.Controllers;

[ApiController]
[Route("api/review")]
public sealed class WebhookController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ReviewQueue _queue;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(IConfiguration config, ReviewQueue queue, ILogger<WebhookController> logger)
    {
        _config = config;
        _queue  = queue;
        _logger = logger;
    }

    [HttpPost]
    public IActionResult Receive([FromBody] AdoWebhookPayload payload)
    {
        // Validate shared secret
        var expectedSecret = _config["Ado:WebhookSecret"];
        var receivedSecret = Request.Headers["X-ADO-Secret"].FirstOrDefault();

        if (string.IsNullOrEmpty(expectedSecret) || receivedSecret != expectedSecret)
        {
            _logger.LogWarning("Webhook received with invalid or missing X-ADO-Secret header.");
            return Unauthorized();
        }

        // Only handle PR created/updated events
        if (!IsPrEvent(payload.EventType))
        {
            _logger.LogInformation("Ignoring non-PR event type: {EventType}", payload.EventType);
            return Ok();
        }

        var job = new ReviewJob(payload);
        _logger.LogInformation(
            "Queuing review for PR #{PrId} in {Repo} ({Project})",
            job.PullRequestId, job.RepositoryName, job.ProjectName);

        _queue.Enqueue(job);

        // Return 202 immediately — ADO service hooks timeout after 10s
        return Accepted();
    }

    private static bool IsPrEvent(string eventType) =>
        eventType is "git.pullrequest.created" or "git.pullrequest.updated";
}
