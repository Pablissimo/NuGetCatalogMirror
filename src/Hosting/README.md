# Hosting a clone of the NuGet Catalog API

You can download a nearly-complete clone of the Catalog API (3GB compressed, 52GB compressed) [from this link](http://bit.ly/nugetcatalogmirror). You can also clone the API contents yourself using the [CrawlNuget](../CrawlNuget) tool, which is incredibly roughly coded and more than likely will need some TLC for your use.

Once you've unpacked the tarball, you'll have a single folder called `api.nuget.org`. You can now serve that folder's contents via Nginx using Docker Compose with the configuration in this folder. You'll need to give Docker read access to that folder.

The `docker-compose.yml` is setup assuming that it is a sibling of the `api.nuget.org` folder. It'll spin up an Nginx host on port 8192 and serve files from http://localhost:8192/v3/index.json onwards.

Any URL in any JSON file from that point on that starts with `https://api.nuget.org` will be rewritten to start `http://localhost:8192`, so you can follow the links within the files served by your cloned API as though it were live. This may mean that some links are rewritten that shouldn't be - you'll have to tweak the setup to your needs, this was as barebones as I could get away with for the project I was completing.

## Why?

I was working on a project to import the NuGet metadata into Neo4j and see if there were interesting things we could explore based on the highly interconnected data NuGet provides. Each Catalog API call takes between 100 and 500ms, and there are 4.8 million ish calls required so the process would have taken 23 days or so to complete. By downloading and mirroring the API locally, the load process took six hours.

## Disclaimer

The clone was pulled and is hosted without the permission or blessing of the NuGet team nor anyone connected with it.