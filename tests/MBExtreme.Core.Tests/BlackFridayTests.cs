using MBExtreme.Core.Notices;
using Xunit;

namespace MBExtreme.Core.Tests;

public class BlackFridayTests
{
    private static DateTimeOffset Brt(int month, int day, int hour, int minute = 0) => new(2026, month, day, hour, minute, 0, TimeSpan.FromHours(-3));

    [Fact]
    public void FollowsTheSiteCalendar()
    {
        Assert.Null(BlackFriday.Active(Brt(11, 23, 23, 59)));
        Assert.Equal(40, BlackFriday.Active(Brt(11, 24, 0))!.Percent);
        Assert.Equal(40, BlackFriday.Active(Brt(11, 26, 23, 59))!.Percent);
        Assert.Equal(50, BlackFriday.Active(Brt(11, 27, 0))!.Percent);
        Assert.Equal(50, BlackFriday.Active(Brt(11, 28, 23, 59))!.Percent);
        Assert.Equal(40, BlackFriday.Active(Brt(11, 29, 0))!.Percent);
        Assert.Equal(40, BlackFriday.Active(Brt(12, 1, 23, 59))!.Percent);
        Assert.Null(BlackFriday.Active(Brt(12, 2, 0)));
        Assert.Equal(50, BlackFriday.Active(new DateTimeOffset(2026, 11, 27, 3, 0, 0, TimeSpan.Zero))!.Percent);   // 00:00 de Brasília em UTC
    }

    [Fact]
    public void HidesForAnnualAndCourtesy()
    {
        var now = Brt(11, 25, 10);
        Assert.True(BlackFriday.ShouldShow(false, null, false, now));                 // grátis / teste
        Assert.True(BlackFriday.ShouldShow(true, now.AddDays(20), false, now));       // mensal
        Assert.False(BlackFriday.ShouldShow(true, now.AddDays(300), false, now));     // anual
        Assert.False(BlackFriday.ShouldShow(true, null, true, now));                  // cortesia sem vencimento
    }

    [Fact]
    public void CountdownAndIds()
    {
        var step = BlackFriday.Active(Brt(11, 27, 0))!;
        Assert.Equal("1d 23:59:00", BlackFriday.Countdown(step, Brt(11, 27, 0, 1)));
        Assert.Equal("00:00:00", BlackFriday.Countdown(step, Brt(12, 5, 0)));
        Assert.NotEqual(BlackFriday.NoticeId(step), BlackFriday.NoticeId(BlackFriday.Active(Brt(11, 25, 0))!));
    }
}
