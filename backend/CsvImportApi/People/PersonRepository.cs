namespace CsvImportApi.People;

public sealed record Person(int Id, string FirstName, string LastName, string Email, string? Phone);

public interface IPersonRepository
{
    bool ExistsByEmail(string normalizedEmail);
    Person Add(string firstName, string lastName, string email, string? phone);
    IReadOnlyList<Person> GetAll();
}

/// <summary>
/// In-memory storage. There is no persistent person store in this repo yet; replace with a real
/// repository (e.g. EF Core) without changing the HTTP contract.
/// </summary>
public sealed class InMemoryPersonRepository : IPersonRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Person> _byEmail = new(StringComparer.Ordinal);
    private readonly List<Person> _people = [];
    private int _nextId = 1;

    public bool ExistsByEmail(string normalizedEmail)
    {
        lock (_gate) return _byEmail.ContainsKey(normalizedEmail);
    }

    public Person Add(string firstName, string lastName, string email, string? phone)
    {
        lock (_gate)
        {
            var person = new Person(_nextId++, firstName, lastName, email, phone);
            _byEmail[PersonCsvContract.NormalizeEmail(email)] = person;
            _people.Add(person);
            return person;
        }
    }

    public IReadOnlyList<Person> GetAll()
    {
        lock (_gate) return _people.ToList();
    }
}
