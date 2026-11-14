using FundaAssignment.Infrastructure.Api.Dtos;
using FundaAssignment.Infrastructure.Api.Mapping;

namespace FundaAssignment.Tests.Infrastructure.Api.Mapping;

[TestFixture]
public class ListingMapperTests
{
    [Test]
    public void Maps_id_agent_id_and_trimmed_agent_name()
    {
        var dto = new FundaObjectDto { GlobalId = 123, AgentId = 45, AgentName = "  Makelaardij Test " };

        var listing = dto.ToDomain();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(listing.Id, Is.EqualTo(123));
            Assert.That(listing.AgentId, Is.EqualTo(45));
            Assert.That(listing.AgentName, Is.EqualTo("Makelaardij Test"));
        }
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Uses_a_placeholder_name_when_the_agent_name_is_missing(string? agentName)
    {
        var dto = new FundaObjectDto { GlobalId = 1, AgentId = 45, AgentName = agentName };

        var listing = dto.ToDomain();

        Assert.That(listing.AgentName, Is.EqualTo("Unknown (45)"));
    }
}
