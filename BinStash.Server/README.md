### Chunk Store
The chunk store is a component of the BinStash server that manages the storage and retrieval of data chunks. Deduplication is only applied to chunks in the same chunk store. This means that chunks with the same hash will only be stored once within a chunk store. Chunk stores can be of type LocalStorage(local disk), S3(Amazon S3, or compatible storage).

### Repository
A repository is a logical grouping of releases. It can be thought of as a project or application. Each repository can have multiple releases, and each release can contain multiple files (binaries).

### TODO
BinStash.Cli.exe release add -v <VERSION/NAME> -r <REPOSITORY_NAME> -n "<YOUR_NOTES_HERE>" -f <ROOT_DIR_OF_RELEASE> -c <COMPONENT_MAPPING_FILE>
1. Get info about the repository
2. Check if the release already exists
3. Scan the directory for files and use ComponentMapping.txt if it exists
4. Chunk the files and upload missing chunks to the chunk store
5. Create a binary release definition file locally
6. Upload the binary release definition file to the server
7. The server will create a release entry in the database
8. On success, print the release ID and URL to the console