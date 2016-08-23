# Veneer

Veneer is a system for linking eWater Source to other applications, including web browsers, scripting tools and other graphical user interface based tools.

Veneer works by hosting a HTTP server within the main Source application and providing access to key data via a RESTful API using JSON encoded data.

Veneer can be used to:

* Build highly customised reports and visualisations for Source models, which can be run live, alongside the model, or packaged for later publication to the web,
* Build tailored Decision Support Systems with custom, HTML based user interfaces,
* Perform ad-hoc scripting tasks while the Source user interface is open, using a tool like R or IPython

## Setup

1. Download and compile the code, or download one of the releases.
2. Load the `FlowMatters.Source.Veneer.dll` plugin using Source's Plugin Manager. You'll need to restart Source.
3. Load a model (rsproj file) and then start Veneer using the Tools|Web Server Monitoring option.
4. Test the connection by opening a web browser and entering the following in the URL bar:

    ```
    http://localhost:9876/network
    ```

This should return a GeoJSON representation of the node-link network for the current model. It'll just be a wall of text, but it indicates that everything is working. You can now use Veneer to control Source, such as by using [veneer-py](https://github.com/flowmatters/veneer-py).

## Options

The Veneer window (Web Server Monitoring) displays several options.

### Port

This controls the network port that Veneer will register on. It defaults to 9876, but if 9876 is taken, for example, by another copy of Source/Veneer, Veneer will try consecutive ports (9877, etc) until it finds one available. You can change the port number and restart the server with the Restart button.

### Allow Remote Connections

Recent versions of Veneer default to local-only connections - ie they will only accept incoming connections from the current machine. The 'Allow Remote Connections' check box changes this, however you will first need to register the URL end points with Windows

#### Registering Veneer as an endpoint

Before using Veneer with the Allow Remote Connections options, you will need to register the port for http use. 

The following command, run as administrator, should do it.

~~~
netsh http add urlacl url=http://+:9876/ user=%USERNAME%
~~~

The `setup_veneer.bat` file, under Extras and included in recent releases, will register 15 ports (starting from 9876) for Veneer. This allows multiple copies of Veneer to be running at the same time.

**Notes:**

* That once you have registered a port in this way, for remote access, you will only be able to run Veneer on this port with the remote connections option switched ON. To go back to local only connections, you will need to select a different port.
* The `netsh` command or the `setup_veneer.bat` file will make the local system changes to accept incoming connections (when run as administrator), however your system or network may still impose firewall rules that prevent such connections.

### Allow Scripts

This option allows Veneer to accept IronPython scripts that get executed within the eWater Source application itself. This can be used to modify the structure of the Source model. This is considered an advanced feature and it also carries security risks when used with the 'Allow Remote Connections' option, so it is off by default.

