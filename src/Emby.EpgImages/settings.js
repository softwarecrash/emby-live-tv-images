define(['baseView', 'loading', 'emby-input', 'emby-button', 'emby-checkbox', 'emby-scroller'], function (BaseView, loading) {
 'use strict';
 var id='a821b0fa-b26e-49b4-aeba-5e40c9ddf6c2';
 function show(page,c){
  ['Enabled','UseTvdbFallback','RunAfterGuide'].forEach(function(k){page.querySelector('#'+k).checked=!!c[k];});
  page.querySelector('#MaxPrograms').value=c.MaxPrograms;
  page.querySelector('#LastResult').textContent=c.LastResult||'Not run yet';
 }
 function View(view,params){
  BaseView.apply(this,arguments);
  view.querySelector('form').addEventListener('submit',function(e){
   e.preventDefault();loading.show();
   ApiClient.getPluginConfiguration(id).then(function(c){
    ['Enabled','UseTvdbFallback','RunAfterGuide'].forEach(function(k){c[k]=view.querySelector('#'+k).checked;});
    c.MaxPrograms=parseInt(view.querySelector('#MaxPrograms').value,10);
    return ApiClient.updatePluginConfiguration(id,c);
   }).then(function(r){loading.hide();Dashboard.processPluginConfigurationUpdateResult(r);},function(){loading.hide();view.querySelector('#LastResult').textContent='Unable to save settings.';});
  });
 }
 Object.assign(View.prototype,BaseView.prototype);
 View.prototype.onResume=function(){
  BaseView.prototype.onResume.apply(this,arguments);loading.show();var page=this.view;
  ApiClient.getPluginConfiguration(id).then(function(c){show(page,c);loading.hide();},function(){loading.hide();page.querySelector('#LastResult').textContent='Unable to load settings.';});
 };
 return View;
});