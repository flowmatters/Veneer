# Veneer

Veneer is a system for linking Source to other applications, including web browsers, scripting tools and other graphical user interface based tools.

Veneer works by hosting a HTTP server within the main Source application and providing access to key data via a RESTful API using JSON encoded data.

Veneer can be used to:

* Build highly customised reports and visualisations for Source models, which can be run live, alongside the model, or packaged for later publication to the web,
* Build tailored Decision Support Systems with custom, HTML based user interfaces,
* Perform ad-hoc scripting tasks while the Source user interface is open, using a tool like R or IPython

## Setup

Download and compile the code, or download one of the releases.

Before using Veneer for the first time, you will need to open a network port, using a command such as the following (Changing joel to your Windows user name)

~~~
netsh http add urlacl url=http://+:9876/ user=joel
~~~
