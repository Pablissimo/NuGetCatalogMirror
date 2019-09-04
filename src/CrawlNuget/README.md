# CrawlNuget

This super-not-supported, bashed-together-in-a-couple-of-hours .NET Core console application starts at a root URL and crawls all URLs found in all documents from that point on.

It's setup to clone the NuGet Catalog API.

This was written and run without permission or blessing of the NuGet team nor anyone connected with it.

## Why?

I needed a local clone of the NuGet catalog (the metadata, not the binaries themselves) for a Neo4j project.

## Output

You can download the 3GB ish result of the clone (taken around about 4th September 2019) as a single .tar.gz file [from this link](http://bit.ly/nugetcatalogmirror).

**WARNING**: It's compressed using the highest gz compression level, and will unpack into around 52GB of disk space and over 4.8 million files across 1.6 million folders. If it breaks your filesystem or your machine then don't say you weren't warned.

## Hosting the clone
Once you've either cloned the files yourself or unzipped the tarball's contents you can serve the file locally via Nginx running in a Docker container. See the [Hosting](../Hosting) folder for the docker-compose.yml file, an Nginx configuration file and a brief note on why you'd want to do this.

## Usage

CrawlNuget.exe &lt;path to output folder&gt;

## Operation

The crawler will start with the root URL configured in Program.cs, and then crawl anything that looks like a URL within any file returned. Links that don't start api.nuget.org will be rejected, so the crawler should stay within the boundary of the API but no guarantees (for example, it'll cheerily follow redirects and not tell you).

Each discovered URL is added to a queue for processing if it hasn't been seen before. A list of already-retrieved URLs is maintained so avoid duplicating network calls.

Every minute or so the queue and the already-retrieved list will be persisted to disk in the output folder as a checkpoint - if you stop and restart the process it will pick up from the last checkpoint.

The console will flood with output messages detailing the retrieval rate and time remaining - both naively calculated over the whole process run so far, and likely not an accurate reflection of anything.

## Known issues

* The crawler will just swallow errors until the process completes
* The path, relative to api.nuget.org will be used for the folder name the files are stored in - if the path can't be translated directly to a Windows path, it'll just error out that file
  * This is less of an issue than you might think - almost all files from the Catalog API also happen to only contain valid path characters, and having the URI trivially mappable to a folder on disk was necessary for my use case
* It'll run with 64 threads by default - on my machine that was pulling around 130 documents a second at around 10Mbps of bandwidth
  * You'll want to alter that number, probably down the way
* A full clone took me around 24 hours

## Licence

MIT
