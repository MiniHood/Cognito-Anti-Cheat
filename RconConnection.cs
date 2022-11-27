using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RustRcon
{
    class IllegalProtocolException : Exception
    {
    }

    /// <summary>
    /// Type of package, can either be Authentication for sending the RCON passphrase, Normal for regular RCON commands or Validation for validating a full package has been received.
    /// </summary>
    public enum PackageType
    {
        Auth,
        Normal,
        Validation
    }

    /// <summary>
    /// Handle requests to and replies from the server.
    /// </summary>
    public class Package
    {
        private static Int32 id_counter = 2; // Make sure not to start at 0 or 1 as those are reserved IDs.
        private Int32 id = -1;
        private int type = -1;
        private string content;
        private Package validationPackage = null;
        private bool complete = false;
        private string response = "";

        private List<Action<Package>> callbacks;

        // Answer
        /// <summary>
        /// Server response constructor.
        /// </summary>
        /// <param name="id">Response ID</param>
        /// <param name="type">Response type</param>
        /// <param name="content">Response content</param>
        public Package(Int32 id, Int32 type, string content)
        {
            this.id = id;
            this.type = type;
            this.response = content;
        }

        // Request
        /// <summary>
        /// Create a new request to send to the server. The content will be sent to the server.
        /// </summary>
        /// <param name="content">Message to send to the server</param>
        /// <param name="type">Message type, Normal, Auth or Validation</param>
        public Package(string content, PackageType type = PackageType.Normal)
        {
            this.id = Package.id_counter++;
            this.content = content;

            this.type = 2;
            if (type == PackageType.Auth)
                this.type = 3;

            if (type != PackageType.Validation)
                this.validationPackage = new Package("", PackageType.Validation);

            this.callbacks = new List<Action<Package>>();
        }

        /// <summary>
        /// Get the ID of this package.
        /// </summary>
        public Int32 ID
        {
            get { return this.id; }
        }

        /// <summary>
        /// Get the raw RCON type of this package.
        /// </summary>
        public int Type
        {
            get { return this.type; }
        }

        /// <summary>
        /// Get the content of this package.
        /// </summary>
        public string Content
        {
            get { return this.content; }
        }

        /// <summary>
        /// Get or set the server response of this package.
        /// </summary>
        public string Response
        {
            get { return this.response; }
            set { this.response = value; }
        }

        /// <summary>
        /// Get the package that is used for validating this package.
        /// </summary>
        public Package ValidationPackage
        {
            get { return this.validationPackage; }
        }

        /// <summary>
        /// Get or set whether or not the package has been validated and is complete and ready to use and start the callback process if necessary.
        /// </summary>
        public bool Complete
        {
            get { return this.complete; }
            set
            {
                this.complete = value;
                if (this.complete)
                    this.callback();
            }
        }

        /// <summary>
        /// Register a callback to be called as soon as the package has validated, with the package itself as parameter.
        /// </summary>
        /// <param name="callback">Action to call</param>
        public void RegisterCallback(Action<Package> callback)
        {
            this.callbacks.Add(callback);
        }

        private void callback()
        {
            foreach (Action<Package> callback in this.callbacks)
                new Task(() => { callback(this); }).Start();
        }
    }

    /// <summary>
    /// Manage the remote connection to a Rust server.
    /// </summary>
    public class Rcon
    {
        private TcpClient client = null;
        private NetworkStream stream = null;
        private Reader reader = null;

        private List<Package> packages;

        /// <summary>
        /// Initialise new remote connection to a specified server.
        /// </summary>
        /// <param name="host">IP, hostname or FQDN of the host</param>
        /// <param name="port">Query port, usualy game port + 1</param>
        /// <param name="rconPassword">RCON passphrase</param>
        public Rcon(string host, int port, string rconPassword)
        {
            if (String.IsNullOrEmpty(host) || String.IsNullOrEmpty(rconPassword) || port < 1 || port > short.MaxValue)
                throw new ArgumentException("Not all arguments have been supplied.");

            this.packages = new List<Package>();

            this.client = new TcpClient();
            this.client.Connect(host, port);

            if (!this.client.Connected)
                throw new SocketException((int)SocketError.ConnectionRefused);

            this.stream = this.client.GetStream();
            this.reader = new Reader(this.stream);
            Task readerTask = new Task(() => { this.reader.Run(this); });
            readerTask.Start();

            this.SendPackage(new Package(rconPassword, PackageType.Auth));
        }

        /// <summary>
        /// Process received packages from server
        /// </summary>
        /// <param name="package">Package from the server</param>
        public void UpdatePackage(Package package)
        {
            if (package.ID == 1)
                return;

            if (package.ID > 1)
            {
                // Validating package?
                Package validator = this.packages.Where(match => match.ValidationPackage != null && match.ValidationPackage.ID == package.ID).FirstOrDefault();
                if (validator != null) // Validator received, completing package
                {
                    if (validator.Complete)
                        throw new IllegalProtocolException();

                    validator.Complete = true;
                    return;
                }

                // Matching package?
                Package matched = this.packages.Where(match => match.ID == package.ID).FirstOrDefault();
                if (matched != null) // Found a matching package
                {
                    matched.Response += package.Response; // Append response to original
                    return;
                }

                // It's either with an ID > 1
                throw new IllegalProtocolException();
            }

            // Passive traffic
            this.packages.Add(package);
        }

        /// <summary>
        /// Read any passive package in order
        /// </summary>
        /// <returns></returns>
        public Package ReadPackage()
        {
            return ReadPackage(0);
        }

        /// <summary>
        /// Read a specific package by ID
        /// </summary>
        /// <param name="id">ID of the package</param>
        /// <returns>Package if found, null of not</returns>
        public Package ReadPackage(int id)
        {
            List<Package> matches = this.packages.Where(match => match.ID == id).ToList();
            if (matches.Count == 0)
                return null;

            Package matched = matches[0];
            if (id == 0 || matched.Complete)
                this.packages.Remove(matched); // Discard if completed

            return matched;
        }

        /// <summary>
        /// Send a package to the server.
        /// </summary>
        /// <param name="package">Package to send to the server.</param>
        /// <returns>ID of the command</returns>
        public int SendPackage(Package package)
        {
            byte[] send = getBytes(package);
            this.stream.Write(send, 0, send.Length);
            if (package.ValidationPackage != null)
            {
                send = getBytes(package.ValidationPackage);
                this.stream.Write(send, 0, send.Length);
            }
            this.stream.Flush();
            this.packages.Add(package);

            return package.ID;
        }

        /// <summary>
        /// Send a simple command to the server.
        /// </summary>
        /// <param name="command">Command to send</param>
        /// <returns>ID of the command</returns>
        public int Send(string command)
        {
            return this.SendPackage(new Package(command));
        }

        /// <summary>
        /// Prepare the byte array to send to the server.
        /// </summary>
        /// <param name="package">Package to be sent</param>
        /// <returns>Byte array to be sent</returns>
        private byte[] getBytes(Package package)
        {
            byte[] id = BitConverter.GetBytes(package.ID);
            byte[] type = BitConverter.GetBytes(package.Type);
            byte[] content = Encoding.UTF8.GetBytes(package.Content);
            int size = id.Length + type.Length + content.Length + 2; // 2x "0x00" on content
            byte[] bsize = BitConverter.GetBytes(size);
            byte[] send = new byte[size + 4];

            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(id);
                Array.Reverse(type);
                Array.Reverse(content);
                Array.Reverse(bsize);
            }

            int position = 0;
            foreach (byte b in bsize)
                send[position++] = b;
            foreach (byte b in id)
                send[position++] = b;
            foreach (byte b in type)
                send[position++] = b;
            foreach (byte b in content)
                send[position++] = b;
            send[position++] = 0;
            send[position++] = 0;

            return send;
        }
    }

    /// <summary>
    /// Continuous reading of RCON packages.
    /// </summary>
    class Reader
    {
        NetworkStream stream = null;

        /// <summary>
        /// Get or set the running state. Set to false to abort.
        /// </summary>
        public bool Running = true;

        /// <summary>
        /// Initialise continuous reader.
        /// </summary>
        /// <param name="stream"></param>
        public Reader(NetworkStream stream)
        {
            this.stream = stream;
        }

        /// <summary>
        /// Start the reader.
        /// </summary>
        public void Run(Rcon rcon)
        {
            while (Running)
            {
                Int32 size = readSize();
                Int32 id = readId();
                Int32 type = readType();
                string body = readEnd();
                if (id == 1) // Ignore all packages with ID 1 (buffer duplicate)
                    continue;
                rcon.UpdatePackage(new Package(id, type, body));
            }
        }

        private byte[] read(int amount)
        {
            byte[] read = new byte[amount];
            int answer = stream.Read(read, 0, read.Length);
            if (answer != read.Length)
            {
                if (answer != 0)
                    Console.WriteLine("# Expected {0} but got {1} bytes.", amount, answer);

                byte[] trim = new byte[answer];
                for (int i = 0; i < answer; i++)
                    trim[i] = read[i];
                read = trim;
            }
            return read;
        }

        private Int32 readInt32()
        {
            byte[] read = this.read(4);

            if (read.Length != 4)
                throw new IllegalProtocolException();

            return BitConverter.ToInt32(read, 0);
        }

        private Int32 readSize()
        {
            byte[] read = this.read(4);

            if (read.Length != 4)
                throw new IllegalProtocolException();

            if (read[0] == 239 && read[1] == 191) // This is an issue in the protocol
            {
                read[0] = read[2];
                read[1] = read[3];
                byte[] add = this.read(2);

                if (add.Length != 2)
                    throw new IllegalProtocolException();

                read[2] = add[0];
                read[3] = add[1];
            }

            return BitConverter.ToInt32(read, 0);
        }

        private Int32 readId()
        {
            return readInt32();
        }

        private Int32 readType()
        {
            return readInt32();
        }

        private string readEnd()
        {
            string body = "";
            while (!body.EndsWith("\x00"))
                body += Encoding.UTF8.GetString(new byte[] { (byte)stream.ReadByte() });
            body = body.Substring(0, body.Length - 1);

            byte[] end = read(1);
            if (end[0] != 0)
                throw new IllegalProtocolException();

            return body;
        }
    }
}
