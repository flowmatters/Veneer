"""
Download all data from a running Veneer server.

Intended to allow offline development and publication of Veneer sites, including web reporting
"""
from urllib2 import urlopen, quote
import json
import os

### Settings
# Source
port = 9876
host = "localhost"
protocol = "http"
retrieve_daily = retreive_monthly = retrieve_annual = True

# Output
destination = "C:\\temp\\veneer_openlayers_demo\\"
zip_destination = "C:\\temp\\veneer_download.zip"

# Misc
print_all = False 
print_urls = True

def mkdirs(directory):
	if not os.path.exists(directory):
		os.makedirs(directory)

def save_data(base_name,data,ext,mode=""):
	base_name = destination + base_name + "."+ext
	directory = os.path.dirname(base_name)
	mkdirs(directory)
	f = open(base_name,"w"+mode)
	f.write(data)
	f.close()

def retrieve_json(url):
	if print_urls:
		print "*** %s ***" % (url)

	text = urlopen(base_url + quote(url)).read()
	save_data(url[1:],text,"json")

	if print_all:
		print json.loads(text)
		print ""
	return json.loads(text)

def retrieve_resource(url,ext):
	if print_urls:
		print "*** %s ***" % (url)

	save_data(url[1:],urlopen(base_url+quote(url)).read(),ext,mode="b")


base_url = "%s://%s:%d" % (protocol,host,port)
mkdirs(destination)

# Process Run list and results
def retrieve_runs():
	run_list = retrieve_json("/runs")
	for run in run_list:
		run_results = retrieve_json(run['RunUrl'])
		for result in run_results['Results']:
			ts_url = result['TimeSeriesUrl']
			if retrieve_daily:
				retrieve_json(ts_url)
			if retreive_monthly:
				retrieve_json(ts_url + "/aggregated/monthly")
			if retrieve_annual:
				retrieve_json(ts_url + "/aggregated/annual")

retrieve_runs()
retrieve_json("/functions")

network = retrieve_json("/network")
for f in network['features']:
	#retrieve_json(f['id'])
	if f['properties']['feature_type'] == 'node':
		retrieve_resource(f['properties']['icon'],'png')