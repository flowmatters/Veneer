"""
Download all data from a running Veneer server.

Intended to allow offline development and publication of Veneer sites, including web reporting
"""
from urllib2 import urlopen, quote
import veneer as v
import os
import sys

class VeneerRetriever(object):
	def __init__(self,destination,port=9876,host='localhost',protocol='http',
				 retrieve_daily=True,retreive_monthly=True,retrieve_annual=True,
				 print_all = False, print_urls = True):
		self.destination = destination
		self.port = port
		self.host = host
		self.protocol = protocol
		self.retrieve_daily = retrieve_daily
		self.retreive_monthly = retreive_monthly
		self.retrieve_annual = retrieve_annual
		self.print_all = print_all
		self.print_urls = print_urls
		self.base_url = "%s://%s:%d" % (protocol,host,port)

	def mkdirs(self,directory):
		if not os.path.exists(directory):
			os.makedirs(directory)

	def save_data(self,base_name,data,ext,mode=""):
		base_name = os.path.join(self.destination,base_name + "."+ext)
		directory = os.path.dirname(base_name)
		self.mkdirs(directory)
		f = open(base_name,"w"+mode)
		f.write(data)
		f.close()
	
	def retrieve_json(self,url,**kwargs):
		if self.print_urls:
			print "*** %s ***" % (url)
	
		text = urlopen(self.base_url + quote(url)).read()
		self.save_data(url[1:],text,"json")
	
		if self.print_all:
			print json.loads(text)
			print ""
		return json.loads(text)
	
	def retrieve_resource(self,url,ext):
		if self.print_urls:
			print "*** %s ***" % (url)
	
		self.save_data(url[1:],urlopen(self.base_url+quote(url)).read(),ext,mode="b")
	
	
	# Process Run list and results
	def retrieve_runs(self):
		run_list = self.retrieve_json("/runs")
		for run in run_list:
			run_results = self.retrieve_json(run['RunUrl'])
			for result in run_results['Results']:
				ts_url = result['TimeSeriesUrl']
				if self.retrieve_daily:
					self.retrieve_json(ts_url)
				if self.retreive_monthly:
					self.retrieve_json(ts_url + "/aggregated/monthly")
				if self.retrieve_annual:
					self.retrieve_json(ts_url + "/aggregated/annual")
	
	def retrieve_all(self,destination,**kwargs):
		self.mkdirs(self.destination)
		self.retrieve_runs()
		self.retrieve_json("/functions")
	
		network = self.retrieve_json("/network")
		for f in network['features']:
			#retrieve_json(f['id'])
			if f['properties']['feature_type'] == 'node':
				self.retrieve_resource(f['properties']['icon'],'png')
	
if __name__ == '__main__':
	# Output
	destination = sys.argv[1] if len(sys.argv)>1 else "C:\\temp\\veneer_download\\"
	zip_destination = sys.argv[2] if len(sys.argv)>2 else None# "C:\\temp\\veneer_download.zip"
	print "Downloading all Veneer data to %s"%destination
	retriever = VeneerRetriever(destination)
	retriever.retrieve_all(destination)

### Settings
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

def retrieve_resource(url,ext):
	if print_urls:
		print "*** %s ***" % (url)

	save_data(url[1:],urlopen(base_url+quote(url)).read(),ext,mode="b")

def retrieve_local(url):
	text = v.retrieve_json(url)
	save_data(url[1:],text,"json")
	return text

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
