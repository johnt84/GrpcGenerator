using System;
using GrpcWizardLibrary;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace GrpcGenerator
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // This is a demo of the GrpcWizard library, which generates a complete gRCP
            // infrastructure from a service and an interface. Each method in the service 
            // must define an input type and return a Task of an output type. 

            // You will end up with a client-side service that you uses .NET types.
            // Conversion to and from gRPC message types is done automatically.

            // The wizard will generate the following files:
            // 
            // 1) a proto file
            // 2) converter classes to convert gRPC message types to .NET types and vice-versa
            // 3) a gRPC server-side service that calls into your existing service 
            // 4) a client-side service that calls the gRPC service, converting types automatically.
            // 5) a README.txt file that has snippets to add to existing files in all three projects.

            // This service and it's interface must be decorated with the [GrpcService] attribute
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
        }
    }

    public class Person
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string Bio { get; set; } = "";
        public string PhotoUrl { get; set; } = "";
    }

    public class GetAllPeopleRequest
    {
    }

    public class GetPersonByIdRequest
    {
        public int Id { get; set; }
    }

    public class PeopleReply
    {
        public List<Person> People { get; set; } = new List<Person>();
    }

    [GrpcService]
    public interface IPeopleService
    {
        Task<PeopleReply> GetAll(GetAllPeopleRequest request);
        Task<Person> GetPersonById(GetPersonByIdRequest request);
    }

    [GrpcService]
    public class PeopleService : IPeopleService
    {
        private List<Person> people = new List<Person>();

        public PeopleService()
        {
            people.Add(new Person { Id = 1, FirstName = "Isadora", LastName = "Jarr" });
            people.Add(new Person { Id = 2, FirstName = "Ben", LastName = "Drinkin" });
            people.Add(new Person { Id = 3, FirstName = "Amanda", LastName = "Reckonwith" });
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
}
