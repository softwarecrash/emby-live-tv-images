using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Tasks;
using System.Text.RegularExpressions;
using System.Text.Json;
using MediaBrowser.Controller.Plugins;

namespace Emby.EpgImages;
public sealed class Configuration : BasePluginConfiguration
{
 public bool Enabled {get;set;} = false;
 public bool ClearImagesOnce {get;set;} = false;
 public bool UseTvdbFallback {get;set;} = true;
 public bool RunAfterGuide {get;set;} = true;
 public int MaxPrograms {get;set;} = 100;
 public int PendingTitles {get;set;}
 public string LastResult {get;set;} = "Not run yet";
}
public sealed class Plugin : BasePlugin<Configuration>, IHasWebPages, IHasThumbImage
{
 public static Plugin Instance {get;private set;}
 public Plugin(IApplicationPaths paths, IXmlSerializer serializer):base(paths,serializer) {Instance=this;}
 public override string Name => "Live TV Images (TMDB/TVDB)";
 public override string Description => "Adds Live TV images using the installed TMDB and TVDB providers.";
 public override Guid Id => new("a821b0fa-b26e-49b4-aeba-5e40c9ddf6c2");
 public MediaBrowser.Model.Drawing.ImageFormat ThumbImageFormat => MediaBrowser.Model.Drawing.ImageFormat.Png;
 public Stream GetThumbImage() => GetType().Assembly.GetManifestResourceStream("Emby.EpgImages.thumb.png");
 public IEnumerable<PluginPageInfo> GetPages() => new[]{
 new PluginPageInfo {Name="epgimages",EmbeddedResourcePath="Emby.EpgImages.settings.html"},
 new PluginPageInfo {Name="epgimagesjs",EmbeddedResourcePath="Emby.EpgImages.settings.js"}};
}
public sealed class GuideHook : IServerEntryPoint
{
 readonly ITaskManager manager;
 public GuideHook(ITaskManager taskManager){manager=taskManager;}
 public void Run(){manager.TaskCompleted+=Completed;}
 void Completed(object sender,TaskCompletionEventArgs args)
 {
  if(args.Result.Key=="RefreshGuide" && Plugin.Instance.Configuration.Enabled && Plugin.Instance.Configuration.RunAfterGuide)
   manager.QueueIfNotRunning<EpgImageTask>();
 }
 public void Dispose(){manager.TaskCompleted-=Completed;}
}
public sealed class CacheRecord
{
 public string Url {get;set;}
 public DateTimeOffset Checked {get;set;}
 public DateTimeOffset LastSeen {get;set;}
}
public sealed class EpgImageTask : IScheduledTask
{
 readonly ILibraryManager library;
 readonly IProviderManager providers;
 public EpgImageTask(ILibraryManager libraryManager,IProviderManager providerManager) {library=libraryManager;providers=providerManager;}
 public string Name => "Update Live TV Images";
 public string Key => "EpgImagesEnrich";
 public string Description => "Adds missing movie and series images from TMDB and TVDB in batches, reuses existing matches and cleans expired cache entries.";
 public string Category => "Live TV";
 public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => new[]{new TaskTriggerInfo{Type="IntervalTrigger",IntervalTicks=TimeSpan.FromMinutes(5).Ticks}};
 static string Normalize(string text) => Regex.Replace((text??"").ToLowerInvariant(),@"[^\p{L}\p{Nd}]","");
 static string Title(LiveTvProgram p) {
  var title=p.IsSeries&&!string.IsNullOrWhiteSpace(p.SeriesName)?p.SeriesName:p.Name;
  return Regex.Replace(title??"",@"\s+(?:Musikshow|Komödie|Dokutainment|Actionfilm|Thriller|Krimiserie|Comedyserie|Sitcom|Animationsserie|Zeichentrickserie)\s*,.*$","",RegexOptions.IgnoreCase).Trim();
 }
 static string CacheKey(LiveTvProgram p) => (p.IsMovie?"movie":"series")+"|"+Normalize(Title(p))+"|"+(p.IsMovie?p.ProductionYear:null);
 static bool Fresh(CacheRecord r) => DateTimeOffset.UtcNow-r.Checked<TimeSpan.FromDays(string.IsNullOrEmpty(r.Url)?1:30);
 public async Task Execute(CancellationToken cancellationToken,IProgress<double> progress)
 {
  var cfg=Plugin.Instance.Configuration;
  if(!cfg.Enabled) {cfg.LastResult="Disabled";Plugin.Instance.SaveConfiguration();return;}
  var available=providers.ImageProviders.Select(p=>p.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
  var missing=new List<string>();
  if(!available.Contains("TheMovieDb",StringComparer.OrdinalIgnoreCase)) missing.Add("TheMovieDb");
  if(cfg.UseTvdbFallback && !available.Contains("TheTVDB",StringComparer.OrdinalIgnoreCase)) missing.Add("TheTVDB");
  if(missing.Count>0) {
   cfg.LastResult="Required plugins missing: "+string.Join(", ",missing)+". Install them and restart Emby, or disable the TVDB fallback in settings.";
   Plugin.Instance.SaveConfiguration();throw new InvalidOperationException(cfg.LastResult);
  }
  if(cfg.ClearImagesOnce) {
   foreach(var item in library.GetItemList(new InternalItemsQuery {IncludeItemTypes=new[]{"LiveTvProgram"}}).OfType<LiveTvProgram>()) {
    cancellationToken.ThrowIfCancellationRequested();
    if(!item.HasImage(ImageType.Primary,0)) continue;
    item.ImageInfos=item.ImageInfos.Where(i=>i.Type!=ImageType.Primary).ToArray();library.UpdateImages(item);
   }
   var oldCache=Path.Combine(Plugin.Instance.DataFolderPath,"matches.json");
   if(File.Exists(oldCache)) File.Move(oldCache,oldCache+".before-reset-"+DateTime.UtcNow.ToString("yyyyMMddHHmmss"));
   cfg.ClearImagesOnce=false;Plugin.Instance.SaveConfiguration();
  }
  int matched=0,saved=0,failed=0,processed=0;
  Directory.CreateDirectory(Plugin.Instance.DataFolderPath);
  var cacheFile=Path.Combine(Plugin.Instance.DataFolderPath,"matches.json");
  var cache=File.Exists(cacheFile)?JsonSerializer.Deserialize<Dictionary<string,CacheRecord>>(File.ReadAllText(cacheFile)):new Dictionary<string,CacheRecord>();
  int lookups=0;
  var all=library.GetItemList(new InternalItemsQuery {IncludeItemTypes=new[]{"LiveTvProgram"}}).OfType<LiveTvProgram>().ToArray();
  int expiredImages=0;
  foreach(var old in all.Where(p=>p.EndDate<DateTimeOffset.UtcNow && p.HasImage(ImageType.Primary,0))) {
   var path=old.PrimaryImagePath??"";
   if(!path.Contains("image.tmdb.org",StringComparison.OrdinalIgnoreCase)&&!path.Contains("thetvdb.com",StringComparison.OrdinalIgnoreCase))continue;
   old.ImageInfos=old.ImageInfos.Where(i=>i.Type!=ImageType.Primary).ToArray();library.UpdateImages(old);expiredImages++;
  }
  var activeKeys=all.Where(p=>p.EndDate>DateTimeOffset.UtcNow).Select(CacheKey).ToHashSet();
  foreach(var pair in cache.ToArray()) {
   if(activeKeys.Contains(pair.Key))pair.Value.LastSeen=DateTimeOffset.UtcNow;
   else if(DateTimeOffset.UtcNow-(pair.Value.LastSeen==default?pair.Value.Checked:pair.Value.LastSeen)>TimeSpan.FromDays(7))cache.Remove(pair.Key);
  }
  var programs=all.Where(p=>!p.HasImage(ImageType.Primary,0)&&!p.IsSports&&!p.IsNews&&(p.IsMovie||p.IsSeries))
   .Where(p=>p.EndDate>DateTimeOffset.UtcNow).OrderBy(p=>p.StartDate).ToArray();
  var results=new List<string>();
  foreach(var p in programs)
  {
   cancellationToken.ThrowIfCancellationRequested();
   try
   {
    string title=Title(p);
    if(string.IsNullOrWhiteSpace(title))continue;
    string key=CacheKey(p);
    cache.TryGetValue(key,out var cached);
    RemoteSearchResult[] exact;
    if(cached!=null && Fresh(cached))
     exact=string.IsNullOrEmpty(cached.Url)?Array.Empty<RemoteSearchResult>():new[]{new RemoteSearchResult{Name=title,ImageUrl=cached.Url}};
    else
    {
    if(lookups>=Math.Clamp(cfg.MaxPrograms,1,200))continue;
    lookups++;
    IEnumerable<RemoteSearchResult> hits;
    if(p.IsMovie) hits=await providers.GetRemoteSearchResults<Movie,MovieInfo>(new RemoteSearchQuery<MovieInfo>{SearchInfo=new MovieInfo{Name=title,Year=p.ProductionYear,MetadataLanguage="de"},SearchProviderName="TheMovieDb"},cancellationToken);
    else hits=await providers.GetRemoteSearchResults<Series,SeriesInfo>(new RemoteSearchQuery<SeriesInfo>{SearchInfo=new SeriesInfo{Name=title,MetadataLanguage="de"},SearchProviderName="TheMovieDb"},cancellationToken);
    exact=hits.Where(h=>Normalize(h.Name)==Normalize(title)&&!string.IsNullOrEmpty(h.ImageUrl)&&(!p.IsMovie||!p.ProductionYear.HasValue||h.ProductionYear==p.ProductionYear)).ToArray();
    if(exact.Length==0 && !p.IsMovie && cfg.UseTvdbFallback)
    {
     var tvdb=await providers.GetRemoteSearchResults<Series,SeriesInfo>(new RemoteSearchQuery<SeriesInfo>{SearchInfo=new SeriesInfo{Name=title,MetadataLanguage="de"},SearchProviderName="TheTVDB"},cancellationToken);
     exact=tvdb.Where(h=>Normalize(h.Name)==Normalize(title)&&!string.IsNullOrEmpty(h.ImageUrl)).ToArray();
    }
    cache[key]=new CacheRecord{Checked=DateTimeOffset.UtcNow,LastSeen=DateTimeOffset.UtcNow,Url=exact.Length==1?exact[0].ImageUrl:null};
    }
    if(exact.Length==1)
    {
     matched++;results.Add(title+" → "+exact[0].Name);
     {
      p.SetImage(new ItemImageInfo {Path=exact[0].ImageUrl,Type=ImageType.Primary,DateModified=DateTimeOffset.UtcNow},0);
      library.UpdateImages(p);
      saved++;
     }
    }
   }
   catch(OperationCanceledException){throw;}
   catch(Exception ex){failed++;results.Add(p.Name+": "+ex.GetType().Name);}
   progress.Report(100.0*(++processed)/Math.Max(1,programs.Length));
  }
  foreach(var extra in cache.OrderByDescending(x=>x.Value.LastSeen).Skip(5000).Select(x=>x.Key).ToArray())cache.Remove(extra);
  cfg.PendingTitles=programs.Where(p=>!p.HasImage(ImageType.Primary,0)&&!string.IsNullOrWhiteSpace(Title(p)))
   .Select(CacheKey).Distinct().Count(k=>!cache.TryGetValue(k,out var record)||!Fresh(record));
  var cacheTemp=cacheFile+".tmp";
  File.WriteAllText(cacheTemp,JsonSerializer.Serialize(cache));File.Move(cacheTemp,cacheFile,true);
  cfg.LastResult=$"{DateTime.UtcNow:u}: {lookups} new searches, {matched} exact matches, {saved} saved, {failed} errors, {cfg.PendingTitles} titles pending, {expiredImages} expired images cleared. "+string.Join("; ",results.Take(10));
  Plugin.Instance.SaveConfiguration();progress.Report(100);
 }
}

