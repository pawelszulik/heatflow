using HeatFlow.Core.Phases;
using HeatFlow.Domain;
using HeatFlow.Infrastructure.HomeAssistant;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeatFlow.Core.Tests;

/// <summary>
/// Histereza klasyfikacji (Faza 1) i dwell zaworów (Faza 2) - mechanizmy przeciw
/// przerzucaniu pokoi między pełnym grzaniem i zamknięciem co 5 minut.
/// Plus test regresyjny najważniejszego ograniczenia: liczba otwartych zaworów
/// (RoomsToHot + RoomsToStay) nigdy nie przekracza MaxValvesOpen, bo przy większej
/// liczbie otwartych obiegów spada wydajność pieca.
/// </summary>
public class HysteresisAndDwellTests
{
    private readonly Phase2ArbitrateService _service;

    public HysteresisAndDwellTests()
    {
        var haClientMock = new Mock<IHomeAssistantClient>();
        var errorLoggerMock = new Mock<IApplicationErrorLogger>();
        errorLoggerMock
            .Setup(x => x.LogAsync(It.IsAny<Exception?>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _service = new Phase2ArbitrateService(
            haClientMock.Object, errorLoggerMock.Object, new Mock<ILogger<Phase2ArbitrateService>>().Object);
    }

    private static Room Pokoj(string name, double score, double deficit = 0.0, int priority = 2)
    {
        return new Room
        {
            Name = name,
            Priority = priority,
            Score = score,
            TempDeficit = deficit,
            TempActual = 20.0,
            TempTarget = 21.0,
            MinimalSetTemperature = 5.0,
            MaximalSetTemperature = 30.0,
            ValveEntityId = $"climate.{name}"
        };
    }

    private static RoomState Historia(string name, DeficitClassification klasyfikacja, int minutTemu)
    {
        return new RoomState
        {
            RoomName = name,
            Classification = (int)klasyfikacja,
            ClassificationSince = DateTime.UtcNow.AddMinutes(-minutTemu),
            RecordedAt = DateTime.UtcNow.AddMinutes(-5)
        };
    }

    // ---------- histereza ----------

    [Fact]
    public void ClassifyDeficit_PokojKtoryGrzal_ZostajeWMaxPonizejProgu()
    {
        var p = TestParameters.Default(); // prog 50, histereza 0,5 * mnoznik 10 = 5 pkt
        var room = Pokoj("salon", score: 47);

        room.ClassifyDeficit(p, DeficitClassification.Max);

        Assert.Equal(DeficitClassification.Max, room.DeficitClassification);
    }

    [Fact]
    public void ClassifyDeficit_PokojKtoryNieGrzal_NieWchodziWMaxPonizejProgu()
    {
        var p = TestParameters.Default();
        var room = Pokoj("salon", score: 47);

        room.ClassifyDeficit(p, DeficitClassification.Stay);

        Assert.Equal(DeficitClassification.Stay, room.DeficitClassification);
    }

    [Fact]
    public void ClassifyDeficit_PoniżejProguMinusHistereza_WypadaZMax()
    {
        var p = TestParameters.Default();
        var room = Pokoj("salon", score: 44); // 50 - 5 = 45, wiec 44 to juz za malo

        room.ClassifyDeficit(p, DeficitClassification.Max);

        Assert.Equal(DeficitClassification.Stay, room.DeficitClassification);
    }

    [Fact]
    public void ClassifyDeficit_DeficytPonadProgBezpieczenstwa_WchodziWMaxNatychmiast()
    {
        var p = TestParameters.Default(); // HysteresisSafetyThreshold = 2,0
        var room = Pokoj("lazienka", score: -500, deficit: 2.5);

        room.ClassifyDeficit(p, DeficitClassification.Disabled);

        Assert.Equal(DeficitClassification.Max, room.DeficitClassification);
    }

    [Fact]
    public void ClassifyDeficit_BezHistoriiZachowujeSieJakPrzedZmiana()
    {
        var p = TestParameters.Default();

        var max = Pokoj("a", score: 51);
        max.ClassifyDeficit(p, null);
        Assert.Equal(DeficitClassification.Max, max.DeficitClassification);

        var stay = Pokoj("b", score: 10);
        stay.ClassifyDeficit(p, null);
        Assert.Equal(DeficitClassification.Stay, stay.DeficitClassification);

        var disabled = Pokoj("c", score: -1);
        disabled.ClassifyDeficit(p, null);
        Assert.Equal(DeficitClassification.Disabled, disabled.DeficitClassification);
    }

    // ---------- dwell ----------

    [Fact]
    public async Task Dwell_PokojZPoprzedniegoCyklu_NieOddajeZaworuOWlos()
    {
        // broniacy ma nizszy Score niz pretendent, ale grzeje od 5 minut przy dwell 20 minut
        var broniacy = Pokoj("broniacy", score: 60);
        var pretendent = Pokoj("pretendent", score: 65);
        foreach (var r in new[] { broniacy, pretendent })
        {
            r.ClassifyDeficit(TestParameters.Default(), null);
        }

        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            Rooms = new List<Room> { broniacy, pretendent },
            PreviousRoomStates = new Dictionary<string, RoomState>
            {
                ["broniacy"] = Historia("broniacy", DeficitClassification.Max, minutTemu: 5)
            }
        };

        var p = TestParameters.Default(minDwellMinutes: 20, maxValvesOpen: 1);

        var result = await _service.ExecuteAsync(state, p);

        Assert.True(result.Success);
        Assert.Single(state.RoomsToHot);
        Assert.Equal("broniacy", state.RoomsToHot[0].Name);
    }

    [Fact]
    public async Task Dwell_PoUplywieCzasu_ZaworPrzechodziDoLepszegoPokoju()
    {
        var broniacy = Pokoj("broniacy", score: 60);
        var pretendent = Pokoj("pretendent", score: 65);
        foreach (var r in new[] { broniacy, pretendent })
        {
            r.ClassifyDeficit(TestParameters.Default(), null);
        }

        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            Rooms = new List<Room> { broniacy, pretendent },
            PreviousRoomStates = new Dictionary<string, RoomState>
            {
                ["broniacy"] = Historia("broniacy", DeficitClassification.Max, minutTemu: 45)
            }
        };

        var result = await _service.ExecuteAsync(state, TestParameters.Default(minDwellMinutes: 20, maxValvesOpen: 1));

        Assert.True(result.Success);
        Assert.Single(state.RoomsToHot);
        Assert.Equal("pretendent", state.RoomsToHot[0].Name);
    }

    [Fact]
    public async Task Dwell_PokojWRealnymDolku_WywlaszczaBroniacego()
    {
        var broniacy = Pokoj("broniacy", score: 60);
        var wychlodzony = Pokoj("wychlodzony", score: 55, deficit: 3.0); // ponad prog bezpieczenstwa 2,0
        foreach (var r in new[] { broniacy, wychlodzony })
        {
            r.ClassifyDeficit(TestParameters.Default(), null);
        }

        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            Rooms = new List<Room> { broniacy, wychlodzony },
            PreviousRoomStates = new Dictionary<string, RoomState>
            {
                ["broniacy"] = Historia("broniacy", DeficitClassification.Max, minutTemu: 2)
            }
        };

        var result = await _service.ExecuteAsync(state, TestParameters.Default(minDwellMinutes: 20, maxValvesOpen: 1));

        Assert.True(result.Success);
        Assert.Single(state.RoomsToHot);
        Assert.Equal("wychlodzony", state.RoomsToHot[0].Name);
    }

    [Fact]
    public async Task Dwell_PokojKtoremuSpadlScorePonizejProgu_JestUtrzymanyPrzyZaworze()
    {
        // Score 46 to poniżej progu 50, więc bez dwell pokój zszedłby do Stay i oddał zawór
        // pretendentowi. Dwell go trzyma, bo grzeje dopiero od 2 minut.
        var broniacy = Pokoj("broniacy", score: 46);
        var pretendent = Pokoj("pretendent", score: 55);
        foreach (var r in new[] { broniacy, pretendent })
        {
            r.ClassifyDeficit(TestParameters.Default(), null);
        }
        Assert.Equal(DeficitClassification.Stay, broniacy.DeficitClassification);

        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            Rooms = new List<Room> { broniacy, pretendent },
            PreviousRoomStates = new Dictionary<string, RoomState>
            {
                ["broniacy"] = Historia("broniacy", DeficitClassification.Max, minutTemu: 2)
            }
        };

        var result = await _service.ExecuteAsync(state, TestParameters.Default(minDwellMinutes: 20, maxValvesOpen: 1));

        Assert.True(result.Success);
        Assert.Single(state.RoomsToHot);
        Assert.Equal("broniacy", state.RoomsToHot[0].Name);
        // podniesiony z powrotem do pełnej nastawy, żeby Faza 3 nie wysłała mu nastawy "trzymaj"
        Assert.Equal(DeficitClassification.Max, state.RoomsToHot[0].DeficitClassification);
        Assert.Equal(30, state.RoomsToHot[0].TemperatureToSet);
    }

    [Fact]
    public async Task Dwell_PrzegrzanyPokoj_ZwalniaSlotOdRazu()
    {
        // Ten sam układ co powyżej, ale broniacy jest cieplejszy niż cel (deficyt poniżej
        // -histerezy). Trzymanie go na pełnej mocy to spalony węgiel, więc dwell go nie broni.
        var broniacy = Pokoj("broniacy", score: 46, deficit: -1.0);
        var pretendent = Pokoj("pretendent", score: 55);
        foreach (var r in new[] { broniacy, pretendent })
        {
            r.ClassifyDeficit(TestParameters.Default(), null);
        }

        var state = new HeatingState
        {
            CurrentTime = DateTime.Now,
            Rooms = new List<Room> { broniacy, pretendent },
            PreviousRoomStates = new Dictionary<string, RoomState>
            {
                ["broniacy"] = Historia("broniacy", DeficitClassification.Max, minutTemu: 2)
            }
        };

        var result = await _service.ExecuteAsync(state, TestParameters.Default(minDwellMinutes: 20, maxValvesOpen: 1));

        Assert.True(result.Success);
        Assert.Single(state.RoomsToHot);
        Assert.Equal("pretendent", state.RoomsToHot[0].Name);
    }

    // ---------- MinValvesOpen ----------

    [Fact]
    public async Task MinValvesOpen_PrzyZerowychDeficytach_OtwieraDokladnieTyleIleParametr()
    {
        var rooms = Enumerable.Range(1, 6).Select(i => Pokoj($"p{i}", score: -10)).ToList();
        foreach (var r in rooms)
        {
            r.ClassifyDeficit(TestParameters.Default(), null); // wszystkie Disabled
        }

        var state = new HeatingState { CurrentTime = DateTime.Now, Rooms = rooms };

        var result = await _service.ExecuteAsync(state, TestParameters.Default(minValvesOpen: 3, maxValvesOpen: 5));

        Assert.True(result.Success);
        Assert.Equal(3, state.RoomsToHot.Count);
        Assert.All(state.RoomsToHot, r => Assert.Equal(DeficitClassification.Max, r.DeficitClassification));
    }

    [Fact]
    public async Task MinValvesOpen_NigdyNiePrzekraczaMaxValvesOpen()
    {
        var rooms = Enumerable.Range(1, 6).Select(i => Pokoj($"p{i}", score: -10)).ToList();
        foreach (var r in rooms)
        {
            r.ClassifyDeficit(TestParameters.Default(), null);
        }

        var state = new HeatingState { CurrentTime = DateTime.Now, Rooms = rooms };

        // konfiguracja sprzeczna: minimum wieksze niz maksimum - limit musi wygrac
        var result = await _service.ExecuteAsync(state, TestParameters.Default(minValvesOpen: 9, maxValvesOpen: 2));

        Assert.True(result.Success);
        Assert.Equal(2, state.RoomsToHot.Count + state.RoomsToStay.Count);
    }

    // ---------- INVARIANT: limit otwartych zaworow ----------

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(15)]
    public async Task Invariant_LiczbaOtwartychZaworowNigdyNiePrzekraczaLimitu(int maxValvesOpen)
    {
        // Losowe, ale powtarzalne zestawy 15 pokoi, z dwell i bez, z pelnym rozrzutem Score.
        var rand = new Random(20260808 + maxValvesOpen);

        for (var przebieg = 0; przebieg < 40; przebieg++)
        {
            var p = TestParameters.Default(
                minDwellMinutes: przebieg % 2 == 0 ? 20 : 0,
                maxValvesOpen: maxValvesOpen,
                minValvesOpen: rand.Next(0, 4));

            var rooms = Enumerable.Range(1, 15)
                .Select(i => Pokoj($"p{i}", score: rand.Next(-120, 200), deficit: rand.NextDouble() * 6 - 2))
                .ToList();

            var historia = new Dictionary<string, RoomState>();
            foreach (var r in rooms)
            {
                r.ClassifyDeficit(p, null);
                if (rand.Next(0, 3) == 0)
                {
                    historia[r.Name] = Historia(r.Name, DeficitClassification.Max, rand.Next(0, 60));
                }
            }

            var state = new HeatingState
            {
                CurrentTime = DateTime.Now,
                Rooms = rooms,
                PreviousRoomStates = historia
            };

            var result = await _service.ExecuteAsync(state, p);

            var otwarte = state.RoomsToHot.Count + state.RoomsToStay.Count;

            Assert.True(result.Success, $"przebieg {przebieg}: faza nie powinna sie wywalic");
            Assert.True(otwarte <= maxValvesOpen,
                $"przebieg {przebieg}: otwartych {otwarte} zaworow przy limicie {maxValvesOpen} " +
                $"(Hot={state.RoomsToHot.Count}, Stay={state.RoomsToStay.Count})");

            // zaden pokoj nie moze byc jednoczesnie w dwoch grupach ani zdublowany w jednej
            var wszystkie = state.RoomsToHot.Concat(state.RoomsToStay).Concat(state.RoomsToDisable).ToList();
            Assert.Equal(wszystkie.Count, wszystkie.Distinct().Count());
            Assert.Equal(rooms.Count, wszystkie.Count);
        }
    }
}
