
(function(){
	v = {};
	
	v.prefix = "";
	v.suffix = "";
	v.img_suffix = "";
	
	v.data_url = function(resource) {
		return v.prefix + resource + v.suffix;
	}

	v.img_url = function(resource) {
		return v.prefix + resource + v.img_suffix;
	}
})();