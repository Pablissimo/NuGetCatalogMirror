# NuGetCatalogMirror

This repo houses a tool to clone the [NuGet Catalog API](https://docs.microsoft.com/en-us/nuget/api/catalog-resource) 
and a Docker Compose and Nginx configuration to host a clone locally.

# Downloads

You can download a nearly complete clone (3GB compressed, 52GB uncompressed - 4.8 million files over 1.6 million folders) 
[from this link](http://bit.ly/nugetcatalogmirror). If you want to pull a more up-to-date version of the Catalog API, you could
amend the crawler app to look at the folder structure and figure out which is the latest catalog page pulled and resume the
process from there.

## Disclaimer

The clone was pulled and is hosted without the permission or blessing of the NuGet team nor anyone connected with it. The tool
is provided for educational purposes and posterity.

## Why?

I was working on a project to import the NuGet metadata into Neo4j and see if there were interesting things we could 
explore based on the highly interconnected data NuGet provides. Each Catalog API call takes between 100 and 500ms, 
and there are 4.8 million ish calls required so the process would have taken 23 days or so to complete. By downloading
and mirroring the API locally, the load process took six hours.
