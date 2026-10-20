using System.Text.Json;
using HeatFlow.Infrastructure.EsterownikMapper;
using Xunit;

namespace HeatFlow.Infrastructure.Tests;

public class EsterownikMapperClientTests
{
    [Fact]
    public void Parse_RealGetAllValuesFragment_ReadsHeatingRegisters()
    {
        // Fragment odpowiedzi GET /readdata/GetAllValues z 2026-10-20 (Retorta-ręczny):
        // pola podtrzymania (pPodOn/pPodOff) i regulatora spalin nie mogą się pomylić z grzaniem.
        const string json = """
            {"pPodOn":7,"pPodOff":27,"pDiffuserMotorSpeed":39,"rrRspEn":"1","rrRspTmax":199,
             "rrRspDiffuserMotorSpeed":28,"rrGPodOn":7,"rrGPodOff":160,"rrGDiffuserMotorSpeed":35,"ecPodOn":"5"}
            """;
        using var doc = JsonDocument.Parse(json);

        var settings = EsterownikMapperClient.Parse(doc.RootElement);

        Assert.Equal(new ControllerFeedSettings(7, 160, 35), settings);
    }

    [Fact]
    public void Parse_MissingRegister_ReturnsNull()
    {
        using var doc = JsonDocument.Parse("""{"rrGPodOn":7,"rrGDiffuserMotorSpeed":35}""");
        Assert.Null(EsterownikMapperClient.Parse(doc.RootElement));
    }

    [Fact]
    public async Task NoBaseUrl_ReturnsNullWithoutGuessingAddress()
    {
        var client = new EsterownikMapperClient(new HttpClient(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<EsterownikMapperClient>.Instance);

        Assert.Null(await client.GetFeedSettingsAsync());
    }
}
