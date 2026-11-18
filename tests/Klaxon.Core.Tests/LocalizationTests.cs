using Klaxon.Core.Localization;
using Klaxon.Core.Scheduling;

namespace Klaxon.Core.Tests;

/// <summary>Checks that the domain produces text in both languages.</summary>
public class LocalizationTests : IDisposable
{
    public void Dispose()
    {
        Loc.Set(AppLanguage.Portuguese);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void English_translates_the_schedule_description()
    {
        Loc.Set(AppLanguage.English);

        var daily = new DailySchedule(new TimeOnly(7, 0));
        var weekly = new WeeklySchedule(WeekDays.Weekdays, new TimeOnly(8, 30));

        Assert.Equal("Every day, 07:00", daily.Describe());
        Assert.Equal("Weekdays, 08:30", weekly.Describe());
    }

    [Fact]
    public void Portuguese_translates_the_schedule_description()
    {
        Loc.Set(AppLanguage.Portuguese);

        var daily = new DailySchedule(new TimeOnly(7, 0));

        Assert.Equal("Todo dia, 07:00", daily.Describe());
    }

    [Fact]
    public void Urgency_name_changes_with_the_language()
    {
        Loc.Set(AppLanguage.English);
        Assert.Equal("Critical", Loc.UrgencyName(Model.UrgencyLevel.Critical));

        Loc.Set(AppLanguage.Portuguese);
        Assert.Equal("Crítico", Loc.UrgencyName(Model.UrgencyLevel.Critical));
    }

    [Theory]
    [InlineData("pt-BR", AppLanguage.Portuguese)]
    [InlineData("pt", AppLanguage.Portuguese)]
    [InlineData("en", AppLanguage.English)]
    [InlineData("en-US", AppLanguage.English)]
    [InlineData(null, AppLanguage.English)]
    [InlineData("", AppLanguage.English)]
    public void Parse_maps_codes_to_languages(string? code, AppLanguage expected)
    {
        Assert.Equal(expected, Loc.Parse(code));
    }
}
