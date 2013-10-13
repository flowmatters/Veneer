      function resultsForVariable(data, variableName) {
      	return resultsThatMatch(data,
      		function(result){ 
      			return result.RecordingVariable == variableName; });
      }

      function resultsForElement(data, elementName) {
      	return resultsThatMatch(data,
      		function(result){ 
      			return result.RecordingElement == elementName;});
      }

      function resultsThatMatch(data, fn) {
	  	var results = [];
      	for( i in data ) {
      		if(fn(data[i])==true) {
      			results.push(data[i]);
      		}
      	}
      	return results;
      }

      function catchments_from_network(data) {
            return filter_feature_type(data,"catchment");
      }

      function nodes_from_network(data) {
            return filter_feature_type(data,"node");
      }

      function links_from_network(data) {
            return filter_feature_type(data,"link");
      }

      function filter_feature_type(data,feature_type) {
            result = {}
            result["type"] ="FeatureCollection"
            result['features'] = []

            for( i in data.features) {
                  f = data.features[i];
                  if(f.properties.feature_type == feature_type)
                        result['features'].push(f);
            }
            return result;
      }

      function find_results_for(full_results,network_element) {
            result = [];
            for( i in full_results.Results) {
                  ts_ref = full_results.Results[i];
                  if(ts_ref.NetworkElement == network_element) {
                        result.push(ts_ref);
                  }
            }

            return result;
      }

      function urlArgs() {
            var result = {};
            var query = location.search.substring(1);
            var pairs = query.split("&");
            for( var i = 0; i < pairs.length; i++ ) {
                  var pos = pairs[i].indexOf('=');
                  if(pos == -1) continue;
                  var name = pairs[i].substring(0,pos);
                  var value = pairs[i].substring(pos+1);
                  value = decodeURIComponent(value);
                  result[name] = value;
            }

            return result;
      }