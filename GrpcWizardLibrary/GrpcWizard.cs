using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Text;
using NuGet.Common;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using System.Threading.Tasks;
using System.Threading;

namespace GrpcWizardLibrary
{
    public static class GrpcWizard
    {
        public static async Task<string> GenerateGrpcInfrastructure(Type ServiceType, string ModelsNameSpace, string ServiceName, string ProtoFileName, string OutputFolder)
        {
            // Is this labeled as a GrpcService?
            var attr = (from a in Attribute.GetCustomAttributes(ServiceType)
                        where a.GetType().Name == "GrpcServiceAttribute"
                        select a).FirstOrDefault();
            if (attr == null)
            {
                // This is NOT a GrpcService
                return "Service is missing the [GrpcService] attribute";
            }

            // This is a GrpcService.
            var protoSb = new StringBuilder();
            protoSb.AppendLine("syntax = \"proto3\";");
            protoSb.AppendLine($"option csharp_namespace = \"{ModelsNameSpace}\";");
            protoSb.AppendLine("");
            protoSb.AppendLine($"service Grpc_{ServiceName} " + "{");

            // load the interface
            var interfaces = ServiceType.GetInterfaces();
            Type serviceInterface = null;
            foreach (var iface in interfaces)
            {
                // Is this labeled as a GrpcService?
                attr = (from a in Attribute.GetCustomAttributes(iface)
                            where a.GetType().Name == "GrpcServiceAttribute"
                            select a).FirstOrDefault();
                if (attr != null)
                {
                    // this interface is the GrpcService interface
                    serviceInterface = iface;
                    break;
                }
            }

            if (serviceInterface == null)
            {
                return "Can not find an interface with the [GrpcService] attribute";
            }

            var messageTypes = new List<Type>();

            var methods = serviceInterface.GetMethods();
            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                if (parameters == null || parameters.Length == 0)
                {
                    return $"Service method {method.Name} requires one input parameter";
                }
                if (parameters.Length > 1)
                {
                    return $"Service method {method.Name} has more than one parameter";
                }
                var inputParam = parameters[0];
                if (method.ReturnParameter.ParameterType.Name != "Task`1")
                {
                    return "Service methods must return a Task<>";
                }
                var returnType = method.ReturnParameter.ParameterType.GenericTypeArguments[0].Name;

                string newline = $"    rpc {method.Name} (Grpc_{inputParam.ParameterType.Name}) returns (Grpc_{returnType});";
                
                // Add parameter types
                if (!messageTypes.Contains(inputParam.ParameterType))
                {
                    messageTypes.Add(inputParam.ParameterType);
                }
                if (!messageTypes.Contains(method.ReturnParameter.ParameterType.GenericTypeArguments[0]))
                {
                    messageTypes.Add(method.ReturnParameter.ParameterType.GenericTypeArguments[0]);
                }

                protoSb.AppendLine(newline);
            }

            // create subfolders
            var ClientOutputFolder = $"{OutputFolder}\\Client\\";
            if (!Directory.Exists(ClientOutputFolder)) Directory.CreateDirectory(ClientOutputFolder);
            var ServerOutputFolder = $"{OutputFolder}\\Server\\";
            if (!Directory.Exists(ServerOutputFolder)) Directory.CreateDirectory(ServerOutputFolder);
            var SharedOutputFolder = $"{OutputFolder}\\Shared\\";
            if (!Directory.Exists(SharedOutputFolder)) Directory.CreateDirectory(SharedOutputFolder);


            protoSb.AppendLine("}");
            
            foreach (Type t in messageTypes)
            {
                protoSb.AppendLine("");
                protoSb.AppendLine($"message Grpc_{t.Name} " + "{");
                var props = t.GetProperties();
                int ordinal = 1;
                foreach (var prop in props)
                {
                    string propertyType = "";
                    
                    if (prop.PropertyType.Name == "Int32")
                    {
                        propertyType = "int32";
                    }
                    else if (prop.PropertyType.Name == "Single")
                    {
                        propertyType = "float";
                    }
                    else if (prop.PropertyType.Name == "Double")
                    {
                        propertyType = "double";
                    }
                    else if (prop.PropertyType.Name == "String")
                    {
                        propertyType = "string";
                    }
                    else if (prop.PropertyType.Name == "List`1")
                    {
                        var listType = prop.PropertyType.GenericTypeArguments[0].Name;

                        if (listType == "Int32")
                            propertyType = "repeated int32";
                        else if (listType == "String")
                            propertyType = "repeated string";
                        else if (listType == "Single")
                            propertyType = "repeated float";
                        else if (listType == "Double")
                            propertyType = "repeated double";
                        else
                            propertyType = "repeated Grpc_" + listType;
                    }
                    else
                    {
                        return $"Unknown Property Type: {prop.PropertyType.Name}";
                    }
                    
                    protoSb.AppendLine($"    {propertyType} {CamelCase(prop.Name)} = {ordinal};");
                    ordinal++;
                }
                protoSb.AppendLine("}");
            }

            if (!Directory.Exists(OutputFolder))
            {
                Directory.CreateDirectory(OutputFolder);
            }

            Console.WriteLine(protoSb.ToString());

            var converterFiles = new List<string>();

            // build converters
            foreach (Type t in messageTypes)
            {
                var converterSb = new StringBuilder();
                string className = $"{t.Name}Converter";
                converterSb.AppendLine("using System.Collections.Generic;");
                converterSb.AppendLine("using System.Linq;");
                converterSb.AppendLine("using System.Text;");
                converterSb.AppendLine("using System.Threading.Tasks;");
                converterSb.AppendLine($"using {ModelsNameSpace};");
                converterSb.AppendLine("");
                converterSb.AppendLine($"namespace {ModelsNameSpace}");
                converterSb.AppendLine("{");
                converterSb.AppendLine($"    public static class {className}");
                converterSb.AppendLine("    {");
                converterSb.AppendLine($"        public static List<Grpc_{t.Name}> From{t.Name}List(List<{t.Name}> list)");
                converterSb.AppendLine("        {");
                converterSb.AppendLine($"            var result = new List<Grpc_{t.Name}>()" + ";");
                converterSb.AppendLine("            foreach (var item in list)");
                converterSb.AppendLine("            {");
                converterSb.AppendLine($"                result.Add(From{t.Name}(item))" + ";");
                converterSb.AppendLine("            }");
                converterSb.AppendLine("            return result;");
                converterSb.AppendLine("        }");
                converterSb.AppendLine("");
                converterSb.AppendLine($"        public static List<{t.Name}> FromGrpc_{t.Name}List(List<Grpc_{t.Name}> list)");
                converterSb.AppendLine("        {");
                converterSb.AppendLine($"            var result = new List<{t.Name}>()" + ";");
                converterSb.AppendLine("            foreach (var item in list)");
                converterSb.AppendLine("            {");
                converterSb.AppendLine($"                result.Add(FromGrpc_{t.Name}(item))" + ";");
                converterSb.AppendLine("            }");
                converterSb.AppendLine("            return result;");
                converterSb.AppendLine("        }");
                converterSb.AppendLine(""); 
                converterSb.AppendLine($"        public static Grpc_{t.Name} From{t.Name}({t.Name} item)");
                converterSb.AppendLine("        {");
                converterSb.AppendLine($"            var result = new Grpc_{t.Name}();");

                var props = t.GetProperties();
                foreach (var prop in props)
                {
                    if (prop.PropertyType.Name == "List`1")
                    {
                        var listType = prop.PropertyType.GenericTypeArguments[0].Name;
                        converterSb.AppendLine($"            var {prop.Name.ToLower()} = {listType}Converter.From{listType}List(item.{prop.Name}.ToList());");
                        converterSb.AppendLine($"            result.{prop.Name}.AddRange({prop.Name.ToLower()});");
                    }
                    else 
                    {
                        converterSb.AppendLine($"            result.{prop.Name} = item.{prop.Name};");
                    }
                }

                converterSb.AppendLine("            return result;");
                converterSb.AppendLine("        }");
                converterSb.AppendLine("");
                converterSb.AppendLine("");
                converterSb.AppendLine($"        public static {t.Name} FromGrpc_{t.Name}(Grpc_{t.Name} item)");
                converterSb.AppendLine("        {");
                converterSb.AppendLine($"            var result = new {t.Name}();");

                props = t.GetProperties();
                foreach (var prop in props)
                {
                    if (prop.PropertyType.Name == "List`1")
                    {
                        var listType = prop.PropertyType.GenericTypeArguments[0].Name;
                        converterSb.AppendLine($"            var {prop.Name.ToLower()} = {listType}Converter.FromGrpc_{listType}List(item.{prop.Name}.ToList());");
                        converterSb.AppendLine($"            result.{prop.Name}.AddRange({prop.Name.ToLower()});");
                    }
                    else
                    {
                        converterSb.AppendLine($"            result.{prop.Name} = item.{prop.Name};");
                    }
                }

                converterSb.AppendLine("            return result;");
                converterSb.AppendLine("        }");
                converterSb.AppendLine("");
                converterSb.AppendLine("    }");
                converterSb.AppendLine("}");
                Console.WriteLine();
                Console.WriteLine(converterSb.ToString());

                string converterFileName = $"{SharedOutputFolder}\\{className}.cs";
                converterFiles.Add($"{className}.cs");
                File.WriteAllText(converterFileName, converterSb.ToString());
            }

            // build the Service file
            var serviceSb = new StringBuilder();

            serviceSb.AppendLine("using Grpc.Core;");
            serviceSb.AppendLine("using Google.Protobuf.WellKnownTypes;");
            serviceSb.AppendLine("using System;");
            serviceSb.AppendLine("using System.Collections.Generic;");
            serviceSb.AppendLine("using System.Linq;");
            serviceSb.AppendLine("using System.Threading.Tasks;");
            serviceSb.AppendLine($"using {ServiceType.Namespace};");
            serviceSb.AppendLine("");
            serviceSb.AppendLine($"namespace {ModelsNameSpace}");
            serviceSb.AppendLine("{");
            serviceSb.AppendLine($"    public class Grpc_{ServiceName}Service : Grpc_{ServiceName}.Grpc_{ServiceName}Base");
            serviceSb.AppendLine("    {");
            serviceSb.AppendLine($"        {ServiceType.Name} {CamelCase(ServiceType.Name)};");
            serviceSb.AppendLine("");
            serviceSb.AppendLine($"        public Grpc_{ServiceName}Service({ServiceType.Name} _{CamelCase(ServiceType.Name)})");
            serviceSb.AppendLine("        {");
            serviceSb.AppendLine($"            {CamelCase(ServiceType.Name)} = _{CamelCase(ServiceType.Name)};");
            serviceSb.AppendLine("        }");
            serviceSb.AppendLine("");

            // original interface methods
            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                var inputParam = parameters[0];
                var returnType = method.ReturnParameter.ParameterType.GenericTypeArguments[0].Name;
                
                serviceSb.AppendLine($"        public override async Task<Grpc_{returnType}> {method.Name}(Grpc_{inputParam.ParameterType.Name} request, ServerCallContext context)");
                serviceSb.AppendLine("        {");
                serviceSb.AppendLine($"            var baseRequest = {inputParam.ParameterType.Name}Converter.FromGrpc_{inputParam.ParameterType.Name}(request);");
                serviceSb.AppendLine($"            var baseResponse = await {CamelCase(ServiceType.Name)}.{method.Name}(baseRequest)" + ";");
                serviceSb.AppendLine($"            var response = {returnType}Converter.From{returnType}(baseResponse);");
                serviceSb.AppendLine("            return response;");
                serviceSb.AppendLine("        }");
                serviceSb.AppendLine("");
            }
            
            serviceSb.AppendLine("    }");
            serviceSb.AppendLine("}");

            Console.WriteLine();
            Console.WriteLine(serviceSb.ToString());

            // Write the client service
            var clientServiceSb = new StringBuilder();
            clientServiceSb.AppendLine($"using {ModelsNameSpace};");
            clientServiceSb.AppendLine("using System;");
            clientServiceSb.AppendLine("using System.Collections.Generic;");
            clientServiceSb.AppendLine("using System.Linq;");
            clientServiceSb.AppendLine("using System.Threading.Tasks;");
            clientServiceSb.AppendLine("");
            clientServiceSb.AppendLine($"public class Grpc{ServiceName}Client");
            clientServiceSb.AppendLine("{");
            clientServiceSb.AppendLine($"    Grpc_{ServiceName}.Grpc_{ServiceName}Client grpc_{ServiceName}Client;");
            clientServiceSb.AppendLine($"    public Grpc{ServiceName}Client(Grpc_{ServiceName}.Grpc_{ServiceName}Client _grpc_{ServiceName}Client)");
            clientServiceSb.AppendLine("    {");
            clientServiceSb.AppendLine($"        grpc_{ServiceName}Client = _grpc_{ServiceName}Client;");
            clientServiceSb.AppendLine("    }");
            clientServiceSb.AppendLine("");

            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                var inputParam = parameters[0];
                var returnType = method.ReturnParameter.ParameterType.GenericTypeArguments[0].Name;
                
                clientServiceSb.AppendLine($"    public async Task<{returnType}> {method.Name}Async({inputParam.ParameterType.Name} request)");
                clientServiceSb.AppendLine("    {");
                clientServiceSb.AppendLine($"        var {CamelCase(inputParam.ParameterType.Name)} = {inputParam.ParameterType.Name}Converter.From{inputParam.ParameterType.Name}(request);");
                clientServiceSb.AppendLine($"        var {CamelCase(returnType)} = await grpc_{ServiceName}Client.{method.Name}Async({CamelCase(inputParam.ParameterType.Name)});");
                clientServiceSb.AppendLine($"        return {returnType}Converter.FromGrpc_{returnType}({CamelCase(returnType)});");
                clientServiceSb.AppendLine("    }");
                clientServiceSb.AppendLine("");
            }
            clientServiceSb.AppendLine("}");

            Console.WriteLine();
            Console.WriteLine(clientServiceSb.ToString());

            // .csproj.text files

            // Get NuGet Versions
            string verGoogleProtobuf = await GetLatestNugetVersion("Google.Protobuf");
            string verGrpcNetClient = await GetLatestNugetVersion("Grpc.Net.Client");
            string verGrpcTools = await GetLatestNugetVersion("Grpc.Tools");
            string verGrpcAspNetCore = await GetLatestNugetVersion("Grpc.AspNetCore");
            string verGrpcAspNetCoreWeb = await GetLatestNugetVersion("Grpc.AspNetCore.Web");

            var sharedSb = new StringBuilder();
            sharedSb.AppendLine("    <ItemGroup>");
            sharedSb.AppendLine($"        <PackageReference Include=\"Google.Protobuf\" Version=\"{verGoogleProtobuf}\" />");
            sharedSb.AppendLine($"        <PackageReference Include=\"Grpc.Net.Client\" Version=\"{verGrpcNetClient}\" />");
            sharedSb.AppendLine($"        <PackageReference Include=\"Grpc.Tools\" Version=\"{verGrpcTools}\" >");
            sharedSb.AppendLine("            <PrivateAssets>all</PrivateAssets>");
            sharedSb.AppendLine("            <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>");
            sharedSb.AppendLine("        </PackageReference>");
            sharedSb.AppendLine("    </ItemGroup>");
            sharedSb.AppendLine("    <ItemGroup>");
            sharedSb.AppendLine("        <SupportedPlatform Include=\"browser\" />");
            sharedSb.AppendLine($"        <Protobuf Include=\"{ProtoFileName}\" />");
            sharedSb.AppendLine("    </ItemGroup>");

            Console.WriteLine();
            Console.WriteLine("shared.csproj");
            Console.WriteLine(sharedSb.ToString());

            var serverSb = new StringBuilder();
            serverSb.AppendLine("    <ItemGroup>");
            serverSb.AppendLine($"        <PackageReference Include=\"Grpc.AspNetCore\" Version=\"{verGrpcAspNetCore}\" />");
            serverSb.AppendLine($"        <PackageReference Include=\"Grpc.AspNetCore.Web\" Version=\"{verGrpcAspNetCoreWeb}\" />");
            serverSb.AppendLine("    </ItemGroup>");

            Console.WriteLine();
            Console.WriteLine("server.csproj");
            Console.WriteLine(serverSb.ToString());

            string verGrpcNetClientWeb = await GetLatestNugetVersion("Grpc.Net.Client.Web");

            var clientSb = new StringBuilder();
            clientSb.AppendLine("    <ItemGroup>");
            clientSb.AppendLine($"        <PackageReference Include=\"Grpc.Net.Client.Web\" Version=\"{verGrpcNetClientWeb}\" />");
            clientSb.AppendLine("    </ItemGroup>");

            Console.WriteLine();
            Console.WriteLine("client.csproj");
            Console.WriteLine(clientSb.ToString());

            var startupSb = new StringBuilder();
            startupSb.AppendLine("    public void ConfigureServices(IServiceCollection services)");
            startupSb.AppendLine("    {");
            startupSb.AppendLine("        services.AddGrpc();");
            startupSb.AppendLine("    }");
            startupSb.AppendLine("");
            startupSb.AppendLine("    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)");
            startupSb.AppendLine("    {");
            startupSb.AppendLine("        app.UseGrpcWeb(); // goes after app.UseRouting()");
            startupSb.AppendLine("        app.UseEndpoints(endpoints =>");
            startupSb.AppendLine("        {");
            startupSb.AppendLine($"            endpoints.MapGrpcService<Grpc_{ServiceName}Service>().EnableGrpcWeb();");
            startupSb.AppendLine("        });");
            startupSb.AppendLine("    }");

            Console.WriteLine();
            Console.WriteLine("Startup.cs:");
            Console.WriteLine(startupSb.ToString());

            var programSb = new StringBuilder();
            programSb.AppendLine($"    using {ModelsNameSpace}" + ";");
            programSb.AppendLine("    using Grpc.Net.Client;");
            programSb.AppendLine("    using Grpc.Net.Client.Web;");
            programSb.AppendLine();
            programSb.AppendLine("    public static async Task Main(string[] args)");
            programSb.AppendLine("    {");
            programSb.AppendLine("        builder.Services.AddSingleton(services =>");
            programSb.AppendLine("        {");
            programSb.AppendLine("            var httpClient = new HttpClient(new GrpcWebHandler(GrpcWebMode.GrpcWeb, new HttpClientHandler()));");
            programSb.AppendLine("            var baseUri = builder.HostEnvironment.BaseAddress;");
            programSb.AppendLine("            var channel = GrpcChannel.ForAddress(baseUri, new GrpcChannelOptions { HttpClient = httpClient });");
            programSb.AppendLine($"            return new Grpc_{ServiceName}.Grpc_{ServiceName}Client(channel);");
            programSb.AppendLine("        });");
            programSb.AppendLine($"        builder.Services.AddScoped<Grpc{ServiceName}Client>();");
            programSb.AppendLine("    }");

            Console.WriteLine();
            Console.WriteLine("Program.cs:");
            Console.WriteLine(programSb.ToString());

            // COMBINE DATA and WRITE FILES

            var readmeSb = new StringBuilder();
            readmeSb.AppendLine("Instructions for modifying your Blazor WebAssembly app to support gRPC");
            readmeSb.AppendLine();

            readmeSb.AppendLine("Shared Project:");
            readmeSb.AppendLine("===============");
            readmeSb.AppendLine("1) Add the following to the Shared project .csproj file:");
            readmeSb.AppendLine();
            readmeSb.Append(sharedSb);
            readmeSb.AppendLine();
            readmeSb.AppendLine($"2) Add the {ProtoFileName} file to the Shared project.");
            readmeSb.AppendLine();
            readmeSb.AppendLine("3) Add the following converter files to the Shared project:");
            readmeSb.AppendLine();
            foreach (var filename in converterFiles)
            {
                readmeSb.AppendLine($"   {filename}");
            }
            readmeSb.AppendLine();
            readmeSb.AppendLine();

            readmeSb.AppendLine("Server Project:");
            readmeSb.AppendLine("===============");
            readmeSb.AppendLine("1) Add the following to the Server project .csproj file:");
            readmeSb.AppendLine();
            readmeSb.Append(serverSb);
            readmeSb.AppendLine("");
            readmeSb.AppendLine($"2) Add the Grpc_{ServiceName}Service.cs file to the Server project.");
            readmeSb.AppendLine();
            readmeSb.AppendLine("3) Add the following to the Server project Startup.cs file:");
            readmeSb.AppendLine();
            readmeSb.Append(startupSb);
            readmeSb.AppendLine();
            readmeSb.AppendLine();

            readmeSb.AppendLine("Client Project:");
            readmeSb.AppendLine("===============");
            readmeSb.AppendLine("1) Add the following to the Client project .csproj file:");
            readmeSb.AppendLine();
            readmeSb.Append(clientSb);
            readmeSb.AppendLine();
            readmeSb.AppendLine($"2) Add the Grpc{ServiceName}Client.cs file to the Client project.");
            readmeSb.AppendLine();
            readmeSb.AppendLine("3) Add the following to the Client project Program.cs file:");
            readmeSb.AppendLine();
            readmeSb.Append(programSb);
            readmeSb.AppendLine();
            readmeSb.AppendLine("4) Add the following @using statement to the Client project _Imports.razor file:");
            readmeSb.AppendLine($"     @using {ModelsNameSpace}");
            readmeSb.AppendLine();
            readmeSb.AppendLine("5) Add the following to the top of any .razor file to access data:");
            readmeSb.AppendLine($"    @inject Grpc{ServiceName}Client {ServiceName}Client");
            readmeSb.AppendLine();

            string readmeFileName = $"{OutputFolder}\\README.txt";
            File.WriteAllText(readmeFileName, readmeSb.ToString());

            string protoFileName = $"{SharedOutputFolder}\\{ProtoFileName}";
            File.WriteAllText(protoFileName, protoSb.ToString());

            var serviceFileName = $"{ServerOutputFolder}\\Grpc_{ServiceName}Service.cs";
            File.WriteAllText(serviceFileName, serviceSb.ToString());

            var clientServiceFIleName = $"{ClientOutputFolder}\\Grpc{ServiceName}Client.cs";
            File.WriteAllText(clientServiceFIleName, clientServiceSb.ToString());

            return "OK";
        }

        private static string CamelCase(string Text)
        {
            return Text.Substring(0, 1).ToLower() + Text.Substring(1);
        }

        static async Task<string> GetLatestNugetVersion(string PackageName)
        {
            ILogger logger = NullLogger.Instance;
            CancellationToken cancellationToken = CancellationToken.None;

            SourceCacheContext cache = new SourceCacheContext();
            SourceRepository repository = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");
            PackageMetadataResource resource = await repository.GetResourceAsync<PackageMetadataResource>();

            IEnumerable<IPackageSearchMetadata> packages = await resource.GetMetadataAsync(
                PackageName,
                includePrerelease: false,
                includeUnlisted: false,
                cache,
                logger,
                cancellationToken);

            return packages.Last().Identity.Version.ToString();
        }
    }
}
