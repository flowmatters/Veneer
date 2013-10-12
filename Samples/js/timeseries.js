	function plot_timeseries(data,parent_id)
	{
		"use strict";

		var container_dimensions = {width:900, height:250},
			margins = {top:10, right:20, bottom: 30, left: 60},
			chart_dimensions = {
				width: container_dimensions.width - margins.left - margins.right,
				height: container_dimensions.height - margins.top - margins.bottom
			};


		var parentObject = d3.select("#"+parent_id);


		var chart = parentObject
			.append("div")
				.attr("class","graph")
			.append("svg")
				.attr("width", container_dimensions.width)
				.attr("height", container_dimensions.height)
			.append("g")
				.attr("transform","translate( " + margins.left + "," + margins.top + ")")
				.attr("class","chartArea");
				
		time_scale = d3.time.scale()
			.range([0,chart_dimensions.width])
			.domain([new Date(data.StartDate), new Date(data.EndDate)]);
			
		flow_scale = d3.scale.linear()
			.range([chart_dimensions.height,0])
			.domain([0,data.Max]);

		var time_axis = d3.svg.axis()
			.scale(time_scale);
			
		var pc_axis = d3.svg.axis()
			.scale(flow_scale)
			.orient("left");
			
		chart.append("g")
			.attr("class", "x axis")
			.attr("transform", "translate(0," + chart_dimensions.height + ")")
			.call(time_axis);
			
		chart.append("g")
			.attr("class", "y axis")
			.call(pc_axis);
			
		d3.select(".y.axis")
			.append("text")
				.attr("text-anchor","middle")
				.text("flow")
				.attr("transform", "rotate(-270,0,0)")
				.attr("x",container_dimensions.height/2)
				.attr("y",50);

		draw_timeseries(data,parent_id);
	}

	function get_time_on_scale(jsonObject)
	{
		newDate = new Date(jsonObject.Date);
//		console.log(newDate);
		return time_scale(newDate);
	}

	function draw_timeseries(data, parent_id){
		//alert("Drawing time series with id:"+id);
		var line = d3.svg.line()
			.x(function(d){return get_time_on_scale(d)})
			.y(function(d){return flow_scale(d.Value)})
			.interpolate("linear");
			
		var g = d3.select("#"+parent_id + " .chartArea")
			.append("g")
				.attr("id", parent_id + "_path")
				.attr("class", "timeseries_path");
				
		g.append("path")
			.attr("d", line(data.Events));
		
/*		g.selectAll("circle")
			.data(data)
			.enter()
			.append("circle")
				.attr("cx", function(d) {return time_scale(d.Date)})
				.attr("cy", function(d) {return flow_scale(d.Value)})
				.attr("r",0);
				
		var enter_duration = 1000;
		g.selectAll("circle")
			.transition()
			.delay(function(d,i){return i / data.length * enter_duration;})
			.attr("r",5)
			.each("end", function(d,i){
				if( i===data.length-1){
					add_label(this,d);
				}
			});
			
		g.selectAll("circle")
			.on("mouseover", function(d){
				d3.select(this)
					.transition()
					.attr("r",9);
			})
			.on("mouseout", function(d,i){
				if( i !== data.length-1) {
					d3.select(this)
						.transition()
						.attr("r",5);
				}
			})
			.on("mouseover.tooltip", function(d){
				d3.select("text#" + d.line_id).remove();
				d3.select("#chart")
					.append("text")
					.text(d.Value)
					.attr("x", time_scale(d.Date) + 10)
					.attr("y", flow_scale(d.Flow) -10)
					.attr("id",d.line_id);
			})
			.on("mouseout.tooltip", function(d){
				d3.select("text#" + d.line_id)
					.transition()
					.duration(500)
					.style("opacity",0)
					.attr("transform","translate(10, -10)")
					.remove();
			});
*/			
	}
	
	function add_label(circle, d){
		d3.select(circle)
			.transition()
			.attr("r",9);
		var g = d3.select("#chart");
		g.append("text")
			.text(d.line_id.split("_")[1])
			.attr("x", time_scale(d.Date))
			.attr("y", flow_scale(d.Value))
			.attr("dy","0.35em")
			.attr("class","linelabel")
			.attr("text-anchor","middle")
			.style("opacity",0)
			.style("fill","white")
			.transition()
				.style("opacity",1);
	}
//})();