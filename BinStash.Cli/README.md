Commands:
- [ ] Login / Logout
- [ ] Repository Management (CRUD)
- [ ] Release Management (CRUD)

### Set up auth and default remote
binrepo login --url https://repo.company.com --token <JWT>

### Create a new repository
binrepo repo create my-app

### List repos
binrepo repo list

### Show repo info
binrepo repo show my-app

### Commit (upload) a release with binaries
binrepo commit --repo my-app --version 1.2.3 ./build/output/

### List releases
binrepo release list --repo my-app

### Download a specific release
binrepo release download --repo my-app --version 1.2.3 --output ./downloads

### Delete a release
binrepo release delete --repo my-app --version 1.2.3
