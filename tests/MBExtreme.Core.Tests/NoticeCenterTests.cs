using MBExtreme.Core.Notices;
using Xunit;

namespace MBExtreme.Core.Tests;

public class NoticeCenterTests
{
    private static Notice N(string id, NoticeKind kind = NoticeKind.Update) => new(id, kind, "Título", "Texto", DateTime.Now);

    [Fact]
    public void Nao_repete_e_respeita_os_tipos_desligados()
    {
        var center = new NoticeCenter(null, k => k != NoticeKind.News);
        Assert.True(center.Add(N("v100")));
        Assert.False(center.Add(N("v100")));
        Assert.False(center.Add(N("novidade", NoticeKind.News)));
        Assert.Single(center.Items);
        Assert.Equal(1, center.Unread);
        center.MarkAllRead();
        Assert.Equal(0, center.Unread);
    }

    [Fact]
    public void Guarda_os_ultimos_50_e_volta_depois_de_reabrir()
    {
        string file = Path.Combine(Path.GetTempPath(), "mbx-avisos-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var center = new NoticeCenter(file, _ => true);
            for (int i = 0; i < 60; i++) center.Add(N("a" + i));
            Assert.Equal(NoticeCenter.Keep, center.Items.Count);
            Assert.Equal("a59", center.Items[0].Id);
            var again = new NoticeCenter(file, _ => true);
            Assert.Equal(NoticeCenter.Keep, again.Items.Count);
            Assert.False(again.Add(N("a59")));
        }
        finally { File.Delete(file); }
    }
}
