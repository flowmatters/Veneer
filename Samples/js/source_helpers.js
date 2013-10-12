      function resultsForVariable(data, variableName)
      {
      	return resultsThatMatch(data,
      		function(result){ 
      			return result.RecordingVariable == variableName; });
      }

      function resultsForElement(data, elementName)
      {
      	return resultsThatMatch(data,
      		function(result){ 
      			return result.RecordingElement == elementName;});
      }

      function resultsThatMatch(data, fn)
      {
	  	var results = [];
      	for( i in data )
      	{
      		if(fn(data[i])==true)
      		{
      			results.push(data[i]);
      		}
      	}
      	return results;
      }