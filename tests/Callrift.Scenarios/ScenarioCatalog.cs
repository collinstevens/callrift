namespace Callrift.Scenarios;

public sealed record Scenario(string Name, string Description, IReadOnlyDictionary<string, string> Before, IReadOnlyDictionary<string, string> After, string[] Options);

public static class ScenarioCatalog
{
    public static IReadOnlyList<Scenario> All { get; } =
    [
        Change("orders", "Primary-constructor DI resolves through interfaces; pricing moves beneath WithTimeout and audit is added.", Orders,
            "var price = await pricing.GetPriceAsync(request.Sku);", "await audit.RecordAsync(request); var price = await WithTimeout(() => pricing.GetPriceAsync(request.Sku));"),
        Change("field-di", "Field-injected interface expands implementation and finds controller root.", """
            interface IService { void Run(); }
            class Service : IService { public void Run() { Before(); } void Before() {} void After() {} }
            class Controller { readonly IService service; public Controller(IService service) { this.service = service; } public void Handle() => service.Run(); }
            """, "Before();", "After();"),
        Change("method-group", "Method-group callback moves under a source wrapper; same-class calls expand.", """
            class Flow { public void Run() { Save(); } void Save() { Store(); } void Store() {} void Wrap(Action callback) => callback(); }
            """, "public void Run() { Save(); }", "public void Run() { Wrap(Save); }"),
        Change("guard", "Existing call moves into a new if guard.", """
            class Flow { public void Run(bool ready) { Save(); } void Save() {} }
            """, "{ Save(); }", "{ if (ready) Save(); }"),
        Change("signature", "A unique method gains a parameter without duplicating its unchanged subtree.", """
            class Flow { public void Run() { Save(); } void Save() {} }
            """, "void Run()", "void Run(int count)"),
        Change("overloads-generics", "Generic extension binding expands the right overload and preserves generic type labels.", """
            class Repo<T> { public void Save(T value) { Before(); } public void Save(int value) { Other(); } void Before() {} void After() {} void Other() {} }
            static class Extensions { public static void Persist<T>(this Repo<T> repo, T value) => repo.Save(value); }
            class Flow { public void Run(Repo<string> repo) => repo.Persist<string>("item"); }
            """, "Before();", "After();"),
        Change("locals-conditional-await", "A local function calls a nullable dependency under conditional access and awaits a source method.", """
            class Flow { public async Task Run(Worker? worker) { await Local(); async Task Local() { worker?.Save(); await Flush(); } } Task Flush() => Task.CompletedTask; }
            class Worker { public void Save() { Before(); } void Before() {} void After() {} }
            """, "Before();", "After();"),
        Change("records-partials", "Partial class members bind across declarations; a record implicit constructor stays visible.", """
            record Order(string Sku);
            partial class Flow { public Order Run() { Check(); return new Order("a"); } }
            partial class Flow { void Check() {} void Audit() {} }
            """, "Check();", "Check(); Audit();"),
        Change("multiple-implementations", "Interface dispatch lists both possible implementations under the caller.", """
            interface IWorker { void Save(); }
            class First : IWorker { public void Save() { Before(); } void Before() {} void After() {} }
            class Second : IWorker { public void Save() {} }
            class Flow(IWorker worker) { public void Run() => worker.Save(); }
            """, "Before();", "After();"),
        Change("abstract-recursion", "Abstract dispatch expands the override and recursion is bounded by canonical identity.", """
            abstract class Worker { public abstract void Save(int depth); }
            class Concrete : Worker { public override void Save(int depth) { if (depth > 0) Save(depth - 1); Before(); } void Before() {} void After() {} }
            class Flow(Worker worker) { public void Run() => worker.Save(1); }
            """, "Before();", "After();"),
        Change("top-level", "Top-level entry expands a route callback, including when the route extension is missing.", """
            var app = new App();
            app.MapPost("/orders", () => new Handler().Handle());
            class App {}
            class Handler { public void Handle() { Before(); } void Before() {} void After() {} }
            """, "Before();", "After();"),
        Change("body-only", "A changed literal remains visible even when no call edge changes.", "class Flow { public int Run() => 1; }", "=> 1", "=> 2"),
        Change("branches", "Switch, catch filters, finally and loop contexts retain source calls.", """
            class Flow { public void Run(int n) { try { switch (n) { case 1: Save(); break; default: break; } } catch (Exception ex) when (Accept(ex)) { Save(); } finally { Flush(); } for (var i = 0; i < n; i++) Flush(); } bool Accept(Exception ex) => true; void Save() {} void Flush() {} void Audit() {} }
            """, "case 1: Save();", "case 1: Audit(); Save();"),
        Change("external-wrapper", "Resolved external Task.Run retains the source call passed in its lambda.", """
            class Flow { public void Run() { Save(); } void Save() {} }
            """, "public void Run() { Save(); }", "public void Run() { Task.Run(() => Save()); }"),
        Change("overload-recursion", "Calling a different overload must not be marked as recursion.", """
            class Flow { public void Run() => Save(1); void Save(int value) => Save(value.ToString()); void Save(string value) { Before(); } void Before() {} void After() {} }
            """, "Before();", "After();"),
        Change("depth", "A change below the depth bound stays visible.", "class Flow { public void Run() => A(); void A() => B(); void B() { Before(); } void Before() {} void After() {} }", "Before();", "After();", ["--depth", "1"]),
        Change("constructor-initializer", "An implicit constructor follows its field initializer into changed source code.", "class Entry { public void Run() { var flow = new Flow(); } } class Flow { readonly int value = Create(); static int Create() { Before(); return 1; } static void Before() {} static void After() {} }", "Before();", "After();"),
        Change("new-recursion", "A newly recursive call has an explicit cycle marker.", "class Flow { public void Run(int n) { Save(); } void Save() {} }", "Save();", "if (n > 0) Run(n - 1); Save();"),
        Change("dispatch-added", "A new implementation changes an unchanged interface call's candidate set.", "interface IWorker { void Save(); } class First : IWorker { public void Save() {} } class Flow(IWorker worker) { public void Run() => worker.Save(); }", "class Flow", "class Second : IWorker { public void Save() {} } class Flow"),
        Change("nameof", "nameof is compile-time syntax and never produces an unresolved call.", "class Flow { public string Run() => nameof(Before); void Before() {} void After() {} }", "nameof(Before)", "nameof(After)"),
        Change("uncalled-interface", "A changed implementation stays a root when no source code calls its interface.", "interface IWorker { void Run(); } class Worker : IWorker { public void Run() { Before(); } void Before() {} void After() {} }", "Before();", "After();"),
        Change("constructor-argument", "Changing only a base constructor argument remains visible.", "class Base { public Base(int value) {} } class Derived : Base { public Derived() : base(1) {} }", "base(1)", "base(2)"),
        Change("inherited-abstract", "Concrete types inherit the implementation of an abstract member through an abstract intermediate base.", "abstract class Base { public abstract void Run(); } abstract class Middle : Base { public override void Run() { Before(); } void Before() {} void After() {} } class Worker : Middle {} class Flow(Base worker) { public void Start() => worker.Run(); }", "Before();", "After();"),
        Change("generic-arity", "Parameterless generic methods with different arities remain distinct symbols.", "class Flow { public void Start() { Run<int>(); Run<int, string>(); } void Run<T>() { Before(); } void Run<T, U>() { Other(); } void Before() {} void After() {} void Other() {} }", "Before();", "After();"),
        Change("recursive-signature", "A signature change retains the old cycle reference for a removed recursive call.", "class Flow { public void Run(int n) { if (n > 0) Run(n - 1); } }", "public void Run(int n) { if (n > 0) Run(n - 1); }", "public void Run() { }"),
        Change("decorator", "A decorator forwards to an interface with several possible implementations; the static candidate cycle is bounded.", "interface IWorker { void Run(); } class Worker : IWorker { public void Run() { Before(); } void Before() {} void After() {} } class Decorator(IWorker inner) : IWorker { public void Run() => inner.Run(); } class Flow(IWorker worker) { public void Start() => worker.Run(); }", "Before();", "After();"),
        Tests(false),
        Tests(true)
    ];

    private static Scenario Change(string name, string description, string source, string oldText, string newText, string[]? options = null) =>
        new(name, description, new Dictionary<string, string> { ["Program.cs"] = source },
            new Dictionary<string, string> { ["Program.cs"] = source.Replace(oldText, newText, StringComparison.Ordinal) }, options ?? []);

    private static Scenario Tests(bool include)
    {
        var before = new Dictionary<string, string>
        {
            ["src/Latest/Flow.cs"] = "class Flow { public void Run() { Before(); } void Before() {} void After() {} }",
            ["src/Latest/App.csproj"] = "<Project />",
            ["specs/Checks.csproj"] = "<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>",
            ["specs/Checks.cs"] = "class Checks { public void Test() => new Flow().Run(); }"
        };
        var after = new Dictionary<string, string>(before) { ["src/Latest/Flow.cs"] = before["src/Latest/Flow.cs"].Replace("Before();", "After();", StringComparison.Ordinal) };
        return new Scenario(include ? "tests-included" : "tests-excluded", "IsTestProject controls callers; Latest is production code.", before, after, include ? ["--tests"] : []);
    }

    private const string Orders = """
        interface IOrderService { Task<Order> PlaceAsync(OrderRequest request); }
        interface IOrderRepository { Task SaveAsync(Order order); }
        interface IPricingClient { Task<decimal> GetPriceAsync(string sku); }
        interface IAuditLog { Task RecordAsync(OrderRequest request); }
        record OrderRequest(string Sku);
        record Order(OrderRequest Request, decimal Price);
        class OrdersController(IOrderService orders) { public Task<Order> Place(OrderRequest request) => orders.PlaceAsync(request); }
        class OrderService(IOrderRepository repository, IPricingClient pricing, IAuditLog audit) : IOrderService
        {
            public async Task<Order> PlaceAsync(OrderRequest request)
            {
                Validate(request);
                var price = await pricing.GetPriceAsync(request.Sku);
                var order = new Order(request, price);
                await repository.SaveAsync(order);
                return order;
            }
            void Validate(OrderRequest request) {}
            Task<T> WithTimeout<T>(Func<Task<T>> action) => action();
        }
        class PricingClient : IPricingClient { public Task<decimal> GetPriceAsync(string sku) => Task.FromResult(10m); }
        class SqlOrderRepository : IOrderRepository { public Task SaveAsync(Order order) => Task.CompletedTask; }
        class AuditLog : IAuditLog { public Task RecordAsync(OrderRequest request) => Task.CompletedTask; }
        """;
}
