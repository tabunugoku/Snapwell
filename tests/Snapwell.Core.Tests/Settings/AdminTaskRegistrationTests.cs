using System.Xml.Linq;
using Snapwell.Core.Settings;

namespace Snapwell.Core.Tests.Settings;

public sealed class AdminTaskRegistrationTests
{
    private const string Sid = "S-1-5-21-123-456-789-1001";
    private const string Executable = @"C:\Program Files\Snapwell\Snapwell.exe";

    [Fact]
    public void ExistingTaskIsDeletedBeforeCreateWithAdministratorOwnerAndReadOnlyUser()
    {
        var store = new FakeStore { Xml = AdminTaskXml.Create(Executable, Sid) };
        new AdminTaskRegistration(store, Sid).Register(Executable, Sid);
        Assert.Equal(new[] { "read", "delete", "create" }, store.Calls);
        Assert.Equal($"O:BAG:BAD:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GR;;;{Sid})", store.Sddl);
    }

    [Fact]
    public void CreationRaceFailsWithoutRetryingAsUpdate()
    {
        var store = new FakeStore { Xml = AdminTaskXml.Create(Executable, Sid), FailCreate = true };
        Assert.Throws<IOException>(() => new AdminTaskRegistration(store, Sid).Register(Executable, Sid));
        Assert.Equal(new[] { "read", "delete", "create" }, store.Calls);
    }

    [Theory]
    [InlineData("Principal")]
    [InlineData("LogonTrigger")]
    public void ForeignPrincipalOrTriggerIsOffAndCannotBeDeletedOrOverwritten(string changedElement)
    {
        const string OtherSid = "S-1-5-21-123-456-789-1002";
        var xml = XElement.Parse(AdminTaskXml.Create(Executable, Sid));
        XNamespace ns = AdminTaskXml.Namespace;
        xml.Descendants(ns + changedElement).Single().Element(ns + "UserId")!.Value = OtherSid;
        var store = new FakeStore { Xml = xml.ToString() };
        var service = new AdminTaskRegistration(store, Sid);
        Assert.Null(service.ReadCommand());
        service.Delete();
        Assert.Throws<InvalidOperationException>(() => service.Register(Executable, Sid));
        Assert.All(store.Calls, call => Assert.Equal("read", call));
        Assert.NotNull(store.Xml);
    }

    [Fact]
    public void TwoUsersOnlyReadAndDeleteTheirOwnSidNamedTask()
    {
        const string OtherSid = "S-1-5-21-123-456-789-1002";
        var store = new ScopedStore();
        var first = new AdminTaskRegistration(store, Sid);
        var second = new AdminTaskRegistration(store, OtherSid);
        first.Register(Executable, Sid);
        Assert.Null(second.ReadCommand());
        second.Delete();
        Assert.Equal(Executable, first.ReadCommand());
        second.Register(Executable, OtherSid);
        first.Delete();
        Assert.Null(first.ReadCommand());
        Assert.Equal(Executable, second.ReadCommand());
        Assert.Contains(OtherSid, Assert.Single(store.Tasks).Key);
    }

    private sealed class ScopedStore : IAdminTaskStore
    {
        internal Dictionary<string, string> Tasks { get; } = [];
        public string? ReadXml(string name) => Tasks.GetValueOrDefault(name);
        public void Delete(string name) => Tasks.Remove(name);
        public void Create(string name, string xml, string sddl) => Tasks.Add(name, xml);
    }

    private sealed class FakeStore : IAdminTaskStore
    {
        internal string? Xml;
        internal string? Sddl;
        internal bool FailCreate;
        internal List<string> Calls { get; } = [];
        public string? ReadXml(string name)
        {
            Calls.Add("read");
            return Xml;
        }

        public void Delete(string name)
        {
            Calls.Add("delete");
            Xml = null;
        }

        public void Create(string name, string xml, string sddl)
        {
            Calls.Add("create");
            if (FailCreate)
            {
                throw new IOException();
            }

            Assert.Null(Xml);
            Xml = xml;
            Sddl = sddl;
        }
    }
}
