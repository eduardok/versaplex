#!/usr/bin/python

#------------------------------------------------------------------------------
#                                  Veranda 
#                              ~--------------~
#
# Original Author: Andrei "Garoth" Thorp <garoth@gmail.com>
#
# Description: This program provides a graphical frontend to using the
#              Versaplex software. The user may provide SQL commands in the
#              editor, and this program will forward those commands over DBus
#              to Versaplexd. Versaplexd will then apply the commands to 
#              whatever database it is configured to use under the surface.
#              As such, this program eventually should be capable of providing
#              a graphical interface to many databases, using Versaplex for
#              abstraction.
#
# Notes:
#   Indentation: I use tabs only, 4 spaces per tab.
#
# Todo:
#   Add version numbers for imports
#------------------------------------------------------------------------------
import sys
import pygtk
import gtk
import gtk.glade
import gtksourceview2 as gtksourceview
import dbus
import time
from Parser import Parser
from Resulter import Resulter
#------------------------------------------------------------------------------
class MainUI:
#------------------------------------------------------------------------------
	""" Generates the GUI for Veranda"""
	#------------------	
	def __init__(self):
	#------------------
		"""Initialize all the UI stuff"""
		# Define some instance variables
		self.name = "Veranda" 			# App name
		self.version = "0.8" 			# Version
		self.newNumbers = [] 			# for naming new tabs
		self.database = DBusSql() 		# SQL Access driver
		self.bottomState = False 		# False: notebookBottom closed

		# Import Glade's XML and load it
		self.gladeTree = gtk.glade.XML("ui.glade")
		dic = {"on_exit":(gtk.mainquit)}
		self.gladeTree.signal_autoconnect(dic)

		# Grab some of the widgets for easy access
		self.window = self.gladeTree.get_widget("window")
		self.vboxMain = self.gladeTree.get_widget("vbox-main")
		self.vpanedEditor = self.gladeTree.get_widget("vpaned-editor")
		self.vpanedPanel = self.gladeTree.get_widget("vpaned-panel")
		self.notebookTop = self.gladeTree.get_widget("notebook-top")
		self.notebookBottom = self.gladeTree.get_widget("notebook-bottom")
		self.buttonRun = self.gladeTree.get_widget("button-run")
		self.buttonNewTab = self.gladeTree.get_widget("button-newtab")
		self.tbuttonHidePanel = self.gladeTree.get_widget("tbutton-hidepanel")
		self.entrySearch = self.gladeTree.get_widget("entry-search")
		self.treeview = self.gladeTree.get_widget("treeview")
		self.resulter = Resulter()

		# Open a first tab (comes with configured editor)
		self.newTab()

		# Set up the hide/show button
		self.rArrow = gtk.Arrow(gtk.ARROW_RIGHT,gtk.SHADOW_IN)
		self.lArrow = gtk.Arrow(gtk.ARROW_LEFT,gtk.SHADOW_IN)
		self.tbuttonHidePanel.add(self.lArrow)

		# Connect events
		self.window.connect("delete_event",gtk.main_quit)
		self.tbuttonHidePanel.connect("toggled",self.hidePanel)
		self.buttonRun.connect("clicked",self.runQuery)
		self.buttonNewTab.connect("clicked",self.newTab)

		# Show things
		self.rArrow.show()
		self.lArrow.show()
		self.window.show()

		# Misc Initializations
		self.initSidebar()
	
	#-----------------
	def initSidebar(self):
	#-----------------
		""" Initializes the sidebar with the tables list and configures it"""
		pass

	#-----------------------	
	def getNewNumber(self):	
	#-----------------------
		""" Get a unique number to number a tab """
		x = 0
		while (True):
			if x in self.newNumbers:
				x = x+1
			else: 
				self.newNumbers.append(x)
				return r"   "+str(x)+r"   "
	
	#--------------------------------------	
	def removeNumber(self,editor,notebook):
	#--------------------------------------
		""" If a given page has a label with an automatic
		number, remove that number from the list of numbers so that
		it can be reassigned to a new fresh tab in the future"""
		self.newNumbers.remove(self.getLabelText(editor,notebook))
	
	#-----------------------------------------------	
	def configureEditor(self,editor,textbuffer):
	#-----------------------------------------------
		"""Sets up a gtksourceview with the common options I want."""
		languagemanager = gtksourceview.LanguageManager()
		textbuffer.set_language(languagemanager.get_language("sql"))
		textbuffer.set_highlight_syntax(True)
		editor.set_show_line_numbers(True)
		editor.set_wrap_mode(gtk.WRAP_WORD_CHAR)

	#-------------------------------------------
	def makeBottomTabMenu(self,number,resulter):
	#-------------------------------------------
		"""Returns an hbox with the title, change button, and close button
		to be put in a tab"""
		hbox = gtk.HBox()
		label = gtk.Label(r"   "+str(number)+r"   ")
		hbox.pack_start(label)
		
		changeIcon = gtk.Image()
		changeIcon.set_from_file("cycle.png")
		buttonMode = gtk.Button(None)
		buttonMode.add(changeIcon)
		hbox.pack_start(buttonMode,False,False,1)

		closeIcon = gtk.Image()
		closeIcon.set_from_file("close.png")
		buttonClose = gtk.Button(None)
		buttonClose.add(closeIcon)
		hbox.pack_start(buttonClose,False,False,1) 

		buttonClose.connect("clicked",self.closeTab,resulter)
		buttonMode.connect("clicked",self.changeMode,resulter)

		changeIcon.show()
		closeIcon.show()
		buttonMode.show()
		label.show()
		buttonClose.show()
		hbox.show()

		return hbox

	#-------------------------------------	
	def showOutput(self,topEditor,result):
	#-------------------------------------
		parser = Parser(result)
		
		if self.bottomState == False:
			self.resulter.update(parser)
			self.notebookBottom.show()
			hbox = self.makeBottomTabMenu("Results", self.resulter)
			self.newTabBottom(self.resulter.getCurrentView(),hbox)
			self.bottomState = True

		else :
			index = self.notebookBottom.page_num(self.resulter.getCurrentView())
			hbox = self.notebookBottom.get_tab_label(
											self.resulter.getCurrentView())
			self.resulter.update(parser)
			self.notebookBottom.remove_page(index)
			self.notebookBottom.insert_page(self.resulter.getCurrentView(),
											hbox,index)
			self.notebookBottom.set_tab_reorderable(
										self.resulter.getCurrentView(),True)
			self.notebookBottom.set_current_page(index)
			
	#----------------------------------
	def newTabBottom(self,widget,hbox):
	#----------------------------------
		"""Creates a new tab on the bottom notebook, with "widget" in the tab
		and "hbox" as the label (not actually a gtk label)"""
		self.notebookBottom.append_page(widget,hbox)
	
	#--------------------------------------	
	def getLabelText(self,editor,notebook):
	#--------------------------------------
		"""Retrieves the label number from notebook with a page which contains 
		the given editor"""
		# FIXME make sure that it can be any child instead of editor
		hbox = notebook.get_tab_label(editor)
		children = hbox.get_children()
		labelText = children[0].get_text()
		labelText = labelText.strip(' ')
		return int(labelText)

	#----------------------#
	#-- CALLBACK METHODS --#
	#----------------------#

	#-----------------------------------	
	def runQuery(self,widget,data=None):
	#-----------------------------------
		"""Uses the database abstraction (initially Dbus)
		To send the query that is in the current window"""
		#inner call returns index, outer returns child
		#FIXME what if there is no current page?
		scrolls = self.notebookTop.get_nth_page(self.notebookTop.
												get_current_page()) 
		editor = scrolls.get_children()[0]
		buffer = editor.get_buffer()
		 #get all text, not including hidden chars
		query = buffer.get_text(buffer.get_start_iter(),
								buffer.get_end_iter(),False)

		print "Running Query:",query

		self.showOutput(editor,self.database.query(query))
	
	#--------------------------------------	
	def newTab(self,widget=None,data=None):
	#--------------------------------------
		"""Open a new editor tab (top)"""

		scrolls = gtk.ScrolledWindow(gtk.Adjustment(),gtk.Adjustment())
		scrolls.set_policy(gtk.POLICY_AUTOMATIC,gtk.POLICY_AUTOMATIC)
		
		textbuffer = gtksourceview.Buffer()
		editor = gtksourceview.View(textbuffer)

		self.configureEditor(editor,textbuffer)

		hbox = gtk.HBox()
		label = gtk.Label(self.getNewNumber())
		hbox.pack_start(label)

		closeIcon = gtk.Image()
		closeIcon.set_from_file("close.png")
		buttonClose = gtk.Button(None)
		buttonClose.add(closeIcon)
		hbox.pack_start(buttonClose,False,False,1) 

		buttonClose.connect("clicked",self.closeTab,scrolls)

		scrolls.add(editor)
		self.notebookTop.append_page(scrolls,hbox)
		self.notebookTop.set_tab_reorderable(scrolls,True) 
		# FIXME why doesn't it set?
		self.notebookTop.set_current_page(self.notebookTop.page_num(scrolls))

		scrolls.show()
		closeIcon.show()
		label.show()
		buttonClose.show()
		hbox.show()
		editor.show()

	#--------------------------------------------
	def closeTab(self,sourceWidget,targetWidget):
	#--------------------------------------------
		# TODO if bottom tab is closed, set bottomState = False
		"""Close a tab. Data is the contents of a notebook you want killed."""
		# Ok. The reason this method is so strange is that it handles both the
		# top and bottom notebooks as well as their relationship. I'll explain.
		
		index = -1
		try:
			index = self.notebookTop.page_num(targetWidget)
		except TypeError:
			pass
		if index != -1:
			self.newNumbers.remove(self.getLabelText(targetWidget, 
														self.notebookTop))
			self.notebookTop.remove_page(index)
			return

		index = self.notebookBottom.page_num(targetWidget.getCurrentView())
		if index != -1:
			self.notebookBottom.remove_page(index)
			self.bottomState = False
			return

		if index == -1:
			print "Worse Than Failure: Lost The Tab!"

	#------------------------------------
	def changeMode(self,widget,resulter):
	#------------------------------------
		"""After a change button is clicked, this makes the notebook tab
	osscroll through the different view modes in a fixed pattern"""
		pageIndex = self.notebookBottom.page_num(resulter.getCurrentView())
		hbox = self.notebookBottom.get_tab_label(resulter.getCurrentView())
		self.notebookBottom.remove_page(pageIndex)
		self.notebookBottom.insert_page(resulter.getNextView(),hbox,pageIndex)
		self.notebookBottom.set_tab_reorderable(resulter.getCurrentView(),True)
		# FIXME why doesn't it set?
		self.notebookBottom.set_current_page(pageIndex)

	#------------------------------------	
	def hidePanel(self,widget,data=None):
	#------------------------------------
		"""Hide/show the side panel"""
		#means it's been pressed and is toggled down
		if widget.get_active() == True: 
			self.treeview.hide()
			self.entrySearch.hide()
			widget.remove(self.lArrow)
			widget.add(self.rArrow)
			#FIXME add resize so it doesn't just waste space
		else:
			self.treeview.show()
			self.entrySearch.show()
			widget.remove(self.rArrow)
			widget.add(self.lArrow)
	
	#--------------------------#
	#-- END CALLBACK METHODS --#
	#--------------------------#

#------------------------------------------------------------------------------
class DBusSql:
#------------------------------------------------------------------------------
	""" Provides abstraction for connecting to an SQL database via 
	DBus and the Versaplex software """
	#TODO let dbus shut down nicely
	#------------------	
	def __init__(self):
	#------------------
		""" Connects to DBus and connects to versaplex"""
		print "~-------------------------------------------~"
		print "| Setting up DBus Connection                |"
		print "| If you're using a non-standard bus,       |"
		print "| export DBUS_SESSION_BUS_ADDRESS           |"
		print "| to listen to it. For testing with WvDBus, |"
		print "| it would be something like                |"
		print "| 'tcp:host=localhost,port=5432'            |"	
		print "~-------------------------------------------~"

		# FIXME Rewrite to use a config file instead of 
		# DBUS_SESSION_BUS_ADDRESS
		self.bus = dbus.SessionBus()
		# FIXME put most of this stuff in a config file
		self.versaplex = self.bus.get_object("com.versabanq.versaplex",
				                 "/com/versabanq/versaplex/db") 
		self.versaplexI = dbus.Interface(self.versaplex,dbus_interface=
				                 "com.versabanq.versaplex.db") 

	#---------------------	
	def query(self,query):
	#---------------------
		return self.versaplexI.ExecRecordset(query)

mainUI=MainUI()
gtk.main()
