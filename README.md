# GrpcGenerator

GrpcGenerator is a .NET Core 5 console app that demonstrates the **GrpcWizard** library, which generates a complete gRCP infrastructure from a service and an interface. Each method in the service must define an input type and return a Task of an output type. 

You will end up with a client-side service that you uses .NET types.
Conversion to and from gRPC message types is done automatically.

The wizard will generate:

- a proto file
- converter class files to convert gRPC message types to .NET types and vice-versa
- a gRPC server-side service that calls into your existing service 
- a client-side service that calls the gRPC service, converting types automatically.
- a README.txt file that has snippets to add to existing files in all three projects.

## Example:

Let's start with a simple model.

```c#
public class Person
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Bio { get; set; } = "";
    public string PhotoUrl { get; set; } = "";
}
```

Before you can generate the gRCP code, you need an interface with at least one method that accepts a custom type and returns a custom type. You can not use primitive types like string or Int32. If you need to pass those, create a class around them.

Let's create some simple classes we can use to send and return data. We're going to add these to a Models folder in the Shared project:

```c#
public class GetAllPeopleRequest
{
	// If you want to pass nothing, you still have to wrap a class around it.
}
```

```c#
public class PeopleReply
{
	public List<Person> People { get; set; } = new List<Person>();
}
```

```c#
public class GetPersonByIdRequest
{
	public int Id { get; set; }
}
```

Now we can create a service interface in the Server project:

```c#
[GrpcService]
public interface IPeopleService
{
	Task<PeopleReply> GetAll(GetAllPeopleRequest request);
	Task<Person> GetPersonById(GetPersonByIdRequest request);
}
```

Notice that the interface is decorated with the `[GrpcService]` attribute. That's so the GrpcWizard can tell which members of the service are relevant.

Here's a simple implementation of `IPeopleService` for the sake of demonstration. Note that the service must ALSO be decorated with the `[GrpcService]` attribute.

```c#
[GrpcService]
public class PeopleService : IPeopleService
{
	private List<Person> people = new List<Person>();

	public PeopleService()
	{
	    people.Add(new Person { Id = 1, FirstName = "Isadora", 
                               LastName = "Jarr" });
	    people.Add(new Person { Id = 2, FirstName = "Ben", 
                               LastName = "Drinkin" });
	    people.Add(new Person { Id = 3, FirstName = "Amanda", 
                               LastName = "Reckonwith" });
	}
	public Task<PeopleReply> GetAll(GetAllPeopleRequest request)
	{
	    var reply = new PeopleReply();
	    // add the entire set to reply.People
	    reply.People.AddRange(people);
	    return Task.FromResult(reply);
	}

	public Task<Person> GetPersonById(GetPersonByIdRequest request)
	{
	    // find the person by request.Id and return
	    var result = (from x in people
			  where x.Id == request.Id
			  select x).FirstOrDefault();

	    return Task.FromResult(result);
	}
}
```

Now you are ready to generate! Here's what the GrpcGenerator console app does:

```c#
// This service and it's interface must be decorated with 
// the [GrpcService] attribute
var ServiceType = typeof(PeopleService);

// the namespace where the protobuf objects will reside
var ModelsFolder = "BlazorGrpcGenerated.Shared.Models";

// the name prefix of the service
var ServiceName = "People";

// the name of the proto file to generate
var ProtoFileName = "people.proto";

// where generated files will be written
string OutputFolder = @"c:\users\carlf\desktop\Output\";

Console.Write("Generating...");

// This returns a string with all the output data
string result = await GrpcWizard.GenerateGrpcInfrastructure(
	ServiceType,
	ModelsFolder,
	ServiceName,
	ProtoFileName,
	OutputFolder);

Console.WriteLine();
Console.WriteLine(result);
```



In the output folder you will see a README.txt file with snippets to put into your existing files: .csproj files, Startup.cs, Program.cs, etc.

You'll also see a *Client* subfolder, a *Server* subfolder, and a *Shared* subfolder with generated files that you must copy into those projects.

