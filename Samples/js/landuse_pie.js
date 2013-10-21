

	function landuse_pie(area_data,element_id) {
		var container_dimensions = {width:300, height:200},
			margins = {top:10, right:110, bottom: 10, left: 10},
			chart_dimensions = {
				width: container_dimensions.width - margins.left - margins.right,
				height: container_dimensions.height - margins.top - margins.bottom
			};
			legend_dimensions = {
				width: container_dimensions.width - chart_dimensions.width - 2 * margins.left,
				height: chart_dimensions.height
			};

		var outerRadius = chart_dimensions.width / 2;
		var innerRadius = 0;

		var arc = d3.svg.arc()
						.innerRadius(innerRadius)
						.outerRadius(outerRadius);

		var chart = d3.select(element_id)
			.append("svg")
				.attr("width", container_dimensions.width)
				.attr("height", container_dimensions.height)
			.append("g")
				.attr("transform","translate( " + margins.left + "," + margins.top + ")")
				.attr("id","lu_chart");
		var color = d3.scale.category10();


		var pie = d3.layout.pie()
						.value(function(d) { return d.area; })
		pied = pie(area_data);

		var arcs = chart.selectAll("g.arc")
						.data(pied)
						.enter()
						.append("g")
						.attr("class","arc")
						.attr("transform","translate("+outerRadius+", "+ outerRadius +")");

		arcs.append("path")
				.attr("fill",function(d,i) {
					return color(i);
				})
				.attr("d",arc);

		arcs.append("text")
				.attr("transform", function(d) {
					return "translate("+arc.centroid(d) + ")";					
				})
				.attr("text-anchor","middle")
				.text(function(d) {
					console.log(d);
					if(d.data.area > 5)
						return d.data.name;
					else
						return "";
				});
	}

	function find_areas_for(feature) {
		result = []

		catchment_name = feature.data.name;
		catchment_name = catchment_name.replace(" ","_").replace("#","")
		prefix = catchment_name + "__"
		suffix = "_areaInSquareMeters"
		match_pattern =  prefix + ".*" + suffix + "$"

		for(i in source_functions) {
			fn = source_functions[i];
			if(fn.Name.match(match_pattern) != null) {
				fu_name = fn.Name.replace("$","").replace(prefix,"").replace(suffix,"")
				result.push({name:fu_name,area:Number(fn.Expression)/ 1000000.0})
			}
		}

		console.log(result);

		return result;
	}
